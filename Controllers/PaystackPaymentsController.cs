// =======================================================
// FILE: Controllers/PaystackPaymentsController.cs
// =======================================================
using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Payments;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Documents;
using LawAfrica.API.Services.Payments;
using LawAfrica.API.Services.Tax; // ✅ VAT math
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/payments/paystack")]
    public class PaystackPaymentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly PaystackService _paystack;
        private readonly PaystackOptions _opts;
        private readonly ILogger<PaystackPaymentsController> _logger;

        private readonly PaymentFinalizerService _finalizer;
        private readonly LegalDocumentPurchaseFulfillmentService _legalDocFulfillment;

        public PaystackPaymentsController(
            ApplicationDbContext db,
            PaystackService paystack,
            IOptions<PaystackOptions> opts,
            ILogger<PaystackPaymentsController> logger,
            PaymentFinalizerService finalizer,
            LegalDocumentPurchaseFulfillmentService legalDocFulfillment
        )
        {
            _db = db;
            _paystack = paystack;
            _opts = opts.Value;
            _logger = logger;

            _finalizer = finalizer;
            _legalDocFulfillment = legalDocFulfillment;
        }

        // ✅ API proxy return: Paystack redirects here, then we redirect to correct client/web return
        [AllowAnonymous]
        [HttpGet("return")]
        public async Task<IActionResult> ReturnToFrontend(
            [FromQuery] string? reference,
            [FromQuery] string? trxref,
            CancellationToken ct = default)
        {
            var r = (reference ?? trxref ?? "").Trim();

            var fallbackFrontendReturn = string.IsNullOrWhiteSpace(_opts.CallbackUrl)
                ? "https://lawafricadigitalhub.pages.dev/payments/paystack/return"
                : _opts.CallbackUrl.Trim();

            fallbackFrontendReturn = fallbackFrontendReturn.TrimEnd('/');

            if (string.IsNullOrWhiteSpace(r))
                return Redirect(fallbackFrontendReturn);

            // ✅ Find intent by reference so we can redirect to mobile deep link if available
            var intent = await _db.PaymentIntents
                .AsNoTracking()
                .Where(x => x.Provider == PaymentProvider.Paystack && x.ProviderReference == r)
                .Select(x => new { x.ClientReturnUrl })
                .FirstOrDefaultAsync(ct);

            var targetBase = intent?.ClientReturnUrl;

            // Fallback to web return if no client return
            if (string.IsNullOrWhiteSpace(targetBase))
                targetBase = fallbackFrontendReturn;

            targetBase = targetBase.Trim();

            // ✅ Append reference safely
            var sep = targetBase.Contains("?") ? "&" : "?";
            return Redirect($"{targetBase}{sep}reference={Uri.EscapeDataString(r)}");
        }

        // Optional: some browsers / flows might use this route name
        [AllowAnonymous]
        [HttpGet("return-visit")]
        public Task<IActionResult> ReturnVisitGetFallback(
            [FromQuery] string? reference,
            [FromQuery] string? trxref,
            CancellationToken ct = default)
        {
            return ReturnToFrontend(reference, trxref, ct);
        }

        [AllowAnonymous]
        [HttpPost("initialize")]
        public async Task<IActionResult> Initialize([FromBody] InitiatePaystackCheckoutRequest request, CancellationToken ct)
        {
            var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;

            int? userId = null;
            string? email = null;

            // ✅ Same rule as MPesa: only PublicSignupFee can be anonymous
            if (request.Purpose != PaymentPurpose.PublicSignupFee)
            {
                if (!isAuthenticated)
                {
                    return Unauthorized(new ProblemDetails
                    {
                        Title = "Authentication required",
                        Detail = "Authentication is required for this payment type.",
                        Status = StatusCodes.Status401Unauthorized
                    });
                }

                userId = HttpContext.User.GetUserId();
            }

            // ✅ Paystack requires email
            if (isAuthenticated && userId.HasValue && userId.Value > 0)
            {
                email = await _db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == userId.Value)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync(ct);

                if (string.IsNullOrWhiteSpace(email))
                    email = request.Email?.Trim();

                if (string.IsNullOrWhiteSpace(email))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Missing email",
                        Detail = "Your account does not have an email address set. Please update your profile email and try again.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }
            }
            else
            {
                // Anonymous flow (PublicSignupFee) must supply email
                email = request.Email?.Trim();

                if (string.IsNullOrWhiteSpace(email))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Email required",
                        Detail = "Paystack requires an email address. Provide Email in the request for public payments.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }
            }

            // ✅ Normalize currency (fallback KES)
            var currency = string.IsNullOrWhiteSpace(request.Currency)
                ? "KES"
                : request.Currency.Trim().ToUpperInvariant();

            // ✅ Amount in MAJOR units
            var amountMajor = request.Amount;

            // ✅ VAT-aware legal doc quote (server truth)
            if (request.Purpose == PaymentPurpose.PublicLegalDocumentPurchase && request.LegalDocumentId.HasValue)
            {
                var quote = await QuoteLegalDocumentAsync(request.LegalDocumentId.Value, ct);
                amountMajor = quote.Gross;
                currency = quote.Currency;

                if (request.Amount > 0 && VatMath.Round2(request.Amount) != quote.Gross)
                {
                    _logger.LogWarning("[PAYSTACK INIT] Amount overridden by VAT quote. request={Req} quoteGross={Gross} docId={DocId}",
                        request.Amount, quote.Gross, request.LegalDocumentId.Value);
                }
            }

            // ✅ Subscription plan truth (server truth) - overrides amount/currency for subscriptions
            if (request.Purpose == PaymentPurpose.PublicProductSubscription ||
                request.Purpose == PaymentPurpose.InstitutionProductSubscription)
            {
                if (!request.ContentProductId.HasValue || request.ContentProductId.Value <= 0)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Missing ContentProductId",
                        Detail = "ContentProductId is required for subscriptions.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                if (!request.ContentProductPriceId.HasValue || request.ContentProductPriceId.Value <= 0)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Missing ContentProductPriceId",
                        Detail = "ContentProductPriceId is required for subscriptions.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                var now = DateTime.UtcNow;

                var plan = await _db.ContentProductPrices
                    .AsNoTracking()
                    .Where(p => p.Id == request.ContentProductPriceId.Value)
                    .Select(p => new
                    {
                        p.Id,
                        p.ContentProductId,
                        p.Amount,
                        p.Currency,
                        p.IsActive,
                        p.EffectiveFromUtc,
                        p.EffectiveToUtc,
                        p.Audience,
                        p.BillingPeriod
                    })
                    .FirstOrDefaultAsync(ct);

                if (plan == null)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Invalid pricing plan",
                        Detail = "Pricing plan not found.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                if (plan.ContentProductId != request.ContentProductId.Value)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Invalid pricing plan",
                        Detail = "Pricing plan does not match ContentProductId.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                if (!plan.IsActive)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Pricing plan inactive",
                        Detail = "Pricing plan is not active.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                if (plan.EffectiveFromUtc.HasValue && plan.EffectiveFromUtc.Value > now)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Pricing plan not yet effective",
                        Detail = "Pricing plan is not yet effective.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                if (plan.EffectiveToUtc.HasValue && plan.EffectiveToUtc.Value < now)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Pricing plan expired",
                        Detail = "Pricing plan has expired.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                var expectedAudience =
                    request.Purpose == PaymentPurpose.PublicProductSubscription
                        ? PricingAudience.Public
                        : PricingAudience.Institution;

                if (plan.Audience != expectedAudience)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Pricing plan mismatch",
                        Detail = "Pricing plan audience mismatch.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                // ✅ Server truth
                amountMajor = plan.Amount;
                currency = string.IsNullOrWhiteSpace(plan.Currency)
                    ? currency
                    : plan.Currency.Trim().ToUpperInvariant();
            }

            if (amountMajor <= 0)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid amount",
                    Detail = "Amount must be greater than zero.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // 1) Create PaymentIntent first (internal source of truth)
            var intent = new PaymentIntent
            {
                Provider = PaymentProvider.Paystack,
                Method = PaymentMethod.Paystack,
                Purpose = request.Purpose,
                Status = PaymentStatus.Pending,

                Amount = amountMajor,
                Currency = currency,

                UserId = userId,
                InstitutionId = request.InstitutionId,
                RegistrationIntentId = request.RegistrationIntentId,
                ContentProductId = request.ContentProductId,
                DurationInMonths = request.DurationInMonths, // legacy (still stored)
                LegalDocumentId = request.LegalDocumentId,

                // ✅ NEW
                ContentProductPriceId = request.ContentProductPriceId,

                CreatedAt = DateTime.UtcNow
            };

            var clientReturn = request.ClientReturnUrl ?? request.CallbackUrl;
            intent.ClientReturnUrl = string.IsNullOrWhiteSpace(clientReturn) ? null : clientReturn.Trim();

            _db.PaymentIntents.Add(intent);
            await _db.SaveChangesAsync(ct);

            // 2) Create reference we control
            var reference = $"LA-{intent.Id}-{Guid.NewGuid():N}"
                .Substring(0, $"LA-{intent.Id}-".Length + 6);

            intent.ProviderReference = reference;
            intent.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // 3) Call Paystack initialize
            try
            {
                // ✅ always API proxy return (then we redirect to client/web in GET /return)
                var callbackUrl = ResolvePaystackCallbackUrl(request.Purpose);

                var init = await _paystack.InitializeTransactionAsync(
                    email: email!,
                    amountMajor: amountMajor,
                    currency: currency,
                    reference: reference,
                    callbackUrl: callbackUrl,
                    ct: ct);

                intent.ProviderReference = init.Reference;
                intent.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                return Ok(new
                {
                    paymentIntentId = intent.Id,
                    authorizationUrl = init.AuthorizationUrl,
                    reference = init.Reference
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Paystack initialization failed for PaymentIntentId={PaymentIntentId}", intent.Id);

                intent.Status = PaymentStatus.Failed;
                intent.ProviderResultDesc = "Paystack initialize failed";
                intent.AdminNotes = SafeTrim(ex.Message, 500);
                intent.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);

                return StatusCode(StatusCodes.Status409Conflict, new ProblemDetails
                {
                    Title = "Paystack initialization failed",
                    Detail = "We could not start the Paystack payment. Please try again. If the problem continues, contact support.",
                    Status = StatusCodes.Status409Conflict
                });
            }
        }

        [AllowAnonymous]
        [HttpGet("intent-by-reference/{reference}")]
        public async Task<IActionResult> GetPaystackIntentByReference(string reference, CancellationToken ct)
        {
            reference = (reference ?? "").Trim();
            if (string.IsNullOrWhiteSpace(reference))
                return BadRequest("Reference is required.");

            var intent = await _db.PaymentIntents
                .AsNoTracking()
                .Where(x => x.Provider == PaymentProvider.Paystack && x.ProviderReference == reference)
                .Select(x => new
                {
                    x.Id,
                    x.Purpose,
                    x.LegalDocumentId,
                    x.RegistrationIntentId,
                    x.ContentProductId,
                    x.InstitutionId,
                    x.ContentProductPriceId
                })
                .FirstOrDefaultAsync(ct);

            if (intent == null)
                return NotFound("Payment intent not found for this reference.");

            return Ok(new
            {
                paymentIntentId = intent.Id,
                meta = new
                {
                    purpose = intent.Purpose.ToString(),
                    legalDocumentId = intent.LegalDocumentId,
                    registrationIntentId = intent.RegistrationIntentId,
                    contentProductId = intent.ContentProductId,
                    institutionId = intent.InstitutionId,
                    contentProductPriceId = intent.ContentProductPriceId
                }
            });
        }

        // ============================================================
        // Optional logging endpoint (kept)
        // ============================================================
        public class ReturnVisitRequest
        {
            public string Reference { get; set; } = string.Empty;
            public string? CurrentUrl { get; set; }
            public string? UserAgent { get; set; }
        }

        [AllowAnonymous]
        [HttpPost("return-visit")]
        public IActionResult ReturnVisitPost([FromBody] ReturnVisitRequest req)
        {
            _logger.LogInformation("[PAYSTACK RETURN VISIT][POST] ref={Ref} user={UserId} url={Url}",
                req?.Reference, User?.Identity?.Name ?? "unknown", req?.CurrentUrl);
            return Ok(new { ok = true });
        }

        // ============================================================
        // ✅ POST /confirm (server-to-server verify + fulfill + finalize)
        // ============================================================
        public class ConfirmPaystackRequest
        {
            public string Reference { get; set; } = string.Empty;
        }

        [AllowAnonymous]
        [HttpPost("confirm")]
        public async Task<IActionResult> Confirm([FromBody] ConfirmPaystackRequest req, CancellationToken ct)
        {
            var reference = (req?.Reference ?? "").Trim();
            if (string.IsNullOrWhiteSpace(reference))
                return BadRequest("Reference is required.");

            var intent = await _db.PaymentIntents
                .FirstOrDefaultAsync(x => x.Provider == PaymentProvider.Paystack && x.ProviderReference == reference, ct);

            if (intent == null)
                return NotFound("PaymentIntent not found.");

            // ✅ If caller is authenticated, enforce ownership
            if (User?.Identity?.IsAuthenticated == true)
            {
                var userId = User.GetUserId();
                if (intent.UserId.HasValue && intent.UserId.Value != userId)
                    return Forbid();
            }

            // ✅ If already success, fulfill+finalize idempotently
            if (intent.Status == PaymentStatus.Success)
            {
                if (intent.Purpose == PaymentPurpose.PublicLegalDocumentPurchase)
                    await _legalDocFulfillment.FulfillAsync(intent);

                await _finalizer.FinalizeIfNeededAsync(intent.Id);

                return Ok(new
                {
                    ok = true,
                    status = intent.Status.ToString(),
                    paymentIntentId = intent.Id,
                    legalDocumentId = intent.LegalDocumentId
                });
            }

            // Verify now (server-to-server truth)
            var verify = await _paystack.VerifyTransactionAsync(reference, ct);

            if (!verify.IsSuccessful)
                return BadRequest($"Paystack verify not successful: {verify.Status}");

            // Validate amount/currency (intent is our server-truth)
            if (!string.Equals(intent.Currency, verify.Currency, StringComparison.OrdinalIgnoreCase))
                return BadRequest("Currency mismatch.");

            if (Math.Abs(intent.Amount - verify.AmountMajor) > 0.0001m)
                return BadRequest("Amount mismatch.");

            // Mark success
            intent.Status = PaymentStatus.Success;
            intent.ProviderTransactionId = verify.ProviderTransactionId;
            intent.ProviderChannel = verify.Channel;
            intent.ProviderPaidAt = verify.PaidAt;
            intent.ProviderResultDesc = "Paystack payment verified (return confirm)";
            intent.ProviderRawJson = verify.RawJson?.Length > 4000 ? verify.RawJson.Substring(0, 4000) : verify.RawJson;
            intent.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            if (intent.Purpose == PaymentPurpose.PublicLegalDocumentPurchase)
                await _legalDocFulfillment.FulfillAsync(intent);

            await _finalizer.FinalizeIfNeededAsync(intent.Id);

            return Ok(new
            {
                ok = true,
                status = intent.Status.ToString(),
                paymentIntentId = intent.Id,
                legalDocumentId = intent.LegalDocumentId
            });
        }

        // =========================
        // Helpers
        // =========================

        // ✅ Always use API proxy return (Paystack redirects here, then we redirect to client/web)
        private string ResolvePaystackCallbackUrl(PaymentPurpose purpose)
            => BuildApiReturnProxyUrl();

        private string BuildApiReturnProxyUrl()
        {
            var apiBase = (_opts.ApiPublicBaseUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiBase))
                apiBase = (_opts.PublicBaseUrl ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(apiBase))
            {
                apiBase = apiBase.TrimEnd('/');
                return $"{apiBase}/api/payments/paystack/return";
            }

            var forwardedProto = Request.Headers["X-Forwarded-Proto"].ToString();
            var forwardedHost = Request.Headers["X-Forwarded-Host"].ToString();

            static string FirstHeaderValue(string v)
                => string.IsNullOrWhiteSpace(v) ? "" : v.Split(',')[0].Trim();

            var scheme = FirstHeaderValue(forwardedProto);
            var host = FirstHeaderValue(forwardedHost);

            if (string.IsNullOrWhiteSpace(scheme)) scheme = Request.Scheme;
            if (string.IsNullOrWhiteSpace(host)) host = Request.Host.Value;

            return $"{scheme}://{host}/api/payments/paystack/return";
        }

        private static string SafeTrim(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            return value.Length <= max ? value : value.Substring(0, max);
        }

        // ✅ VAT Quote helper (legal docs)
        private async Task<(decimal Net, decimal Vat, decimal Gross, string Currency)> QuoteLegalDocumentAsync(int legalDocumentId, CancellationToken ct)
        {
            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .Include(d => d.VatRate)
                .Where(d => d.Id == legalDocumentId)
                .Select(d => new
                {
                    d.Id,
                    d.PublicPrice,
                    d.PublicCurrency,
                    d.AllowPublicPurchase,
                    d.Status,
                    d.VatRateId,
                    VatRatePercent = d.VatRate != null ? d.VatRate.RatePercent : 0m,
                    d.IsTaxInclusive
                })
                .FirstOrDefaultAsync(ct);

            if (doc == null || doc.Status != LegalDocumentStatus.Published)
                throw new InvalidOperationException("Document not found or unpublished.");

            if (!doc.AllowPublicPurchase || doc.PublicPrice == null || doc.PublicPrice <= 0)
                throw new InvalidOperationException("This document is not available for purchase.");

            var price = doc.PublicPrice.Value;
            var rate = doc.VatRateId.HasValue ? doc.VatRatePercent : 0m;

            decimal net, vat, gross;

            if (rate <= 0m)
            {
                net = VatMath.Round2(price);
                vat = 0m;
                gross = VatMath.Round2(price);
            }
            else if (doc.IsTaxInclusive)
            {
                (net, vat, gross) = VatMath.FromGrossInclusive(price, rate);
            }
            else
            {
                (net, vat, gross) = VatMath.FromNet(price, rate);
            }

            var currency = string.IsNullOrWhiteSpace(doc.PublicCurrency)
                ? "KES"
                : doc.PublicCurrency!.Trim().ToUpperInvariant();

            return (net, vat, gross, currency);
        }
    }
}
