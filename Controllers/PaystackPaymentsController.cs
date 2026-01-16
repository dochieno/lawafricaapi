using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models.DTOs.Payments;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Services.Payments;
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

        public PaystackPaymentsController(
            ApplicationDbContext db,
            PaystackService paystack,
            IOptions<PaystackOptions> opts,
            ILogger<PaystackPaymentsController> logger)
        {
            _db = db;
            _paystack = paystack;
            _opts = opts.Value;
            _logger = logger;
        }

        // ✅ API proxy return: Paystack redirects here (GET) then we redirect to frontend return page.
        [AllowAnonymous]
        [HttpGet("return")]
        public IActionResult ReturnToFrontend([FromQuery] string? reference, [FromQuery] string? trxref)
        {
            var r = (reference ?? trxref ?? "").Trim();

            var frontendReturn = "https://lawafricadigitalhub.vercel.app/payments/paystack/return";

            if (string.IsNullOrWhiteSpace(r))
                return Redirect(frontendReturn);

            return Redirect($"{frontendReturn}?reference={Uri.EscapeDataString(r)}");
        }

        /// <summary>
        /// Creates a PaymentIntent + initializes Paystack transaction, returns authorization_url to redirect user.
        /// </summary>
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

            if (request.Amount <= 0)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid amount",
                    Detail = "Amount must be greater than zero.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var currency = string.IsNullOrWhiteSpace(request.Currency)
                ? "KES"
                : request.Currency.Trim().ToUpperInvariant();

            // 1) Create PaymentIntent first (internal source of truth)
            var intent = new PaymentIntent
            {
                Provider = PaymentProvider.Paystack,
                Method = PaymentMethod.Paystack,
                Purpose = request.Purpose,
                Status = PaymentStatus.Pending,

                Amount = request.Amount,
                Currency = currency,

                UserId = userId,
                InstitutionId = request.InstitutionId,
                RegistrationIntentId = request.RegistrationIntentId,
                ContentProductId = request.ContentProductId,
                DurationInMonths = request.DurationInMonths,
                LegalDocumentId = request.LegalDocumentId,

                CreatedAt = DateTime.UtcNow
            };

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
                // ✅ CRITICAL FIX:
                // - Signup: keep existing behavior (working)
                // - Non-signup: force callback to API proxy GET /return (anonymous), then redirect to frontend
                var callbackUrl = ResolvePaystackCallbackUrl(request.Purpose);

                var init = await _paystack.InitializeTransactionAsync(
                    email: email!,
                    amountMajor: request.Amount,
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
                .Where(x =>
                    x.Provider == PaymentProvider.Paystack &&
                    x.ProviderReference == reference)
                .Select(x => new
                {
                    x.Id,
                    x.Purpose,
                    x.LegalDocumentId,
                    x.RegistrationIntentId,
                    x.ContentProductId,
                    x.InstitutionId
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
                    institutionId = intent.InstitutionId
                }
            });
        }

        // =========================
        // Helpers
        // =========================

        private string ResolvePaystackCallbackUrl(PaymentPurpose purpose)
        {
            // ✅ DO NOT mess signup flow
            if (purpose == PaymentPurpose.PublicSignupFee)
            {
                var fallbackFrontendReturn = "https://lawafricadigitalhub.vercel.app/payments/paystack/return";

                var configured = (_opts.CallbackUrl ?? "").Trim();
                if (string.IsNullOrWhiteSpace(configured)) return fallbackFrontendReturn;

                // Guard against the exact failure you hit (misconfigured to /return-visit)
                if (configured.Contains("return-visit", StringComparison.OrdinalIgnoreCase))
                    return fallbackFrontendReturn;

                // Guard against mistakenly pointing signup callback to API proxy (not desired)
                if (configured.Contains("/api/payments/paystack/return", StringComparison.OrdinalIgnoreCase))
                    return fallbackFrontendReturn;

                return configured;
            }

            // ✅ Non-signup purchases: always go through API proxy return
            return BuildApiReturnProxyUrl();
        }

        private string BuildApiReturnProxyUrl()
        {
            // Prefer a canonical public base URL (works behind proxies/CDNs)
            var baseUrl = (_opts.PublicBaseUrl ?? "").Trim().TrimEnd('/');

            if (!string.IsNullOrWhiteSpace(baseUrl))
                return $"{baseUrl}/api/payments/paystack/return";

            // Fallback (may be wrong if behind a proxy, but better than nothing)
            return $"{Request.Scheme}://{Request.Host.Value}/api/payments/paystack/return";
        }

        private static string SafeTrim(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }
}
