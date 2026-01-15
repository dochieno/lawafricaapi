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

        // ✅ If you ever decide to route callback through API first (optional).
        // Paystack can redirect to:
        //   https://lawafricaapi.onrender.com/api/payments/paystack/return?reference=...&trxref=...
        // Then we redirect to frontend:
        //   https://lawafricadigitalhub.vercel.app/payments/paystack/return?reference=...&trxref=...
        [AllowAnonymous]
        [HttpGet("return")]
        public IActionResult ReturnToFrontend([FromQuery] string? reference, [FromQuery] string? trxref)
        {
            // Prefer reference, fallback trxref
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
            // ✅ Same rule as MPesa: only PublicSignupFee can be anonymous
            var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;

            int? userId = null;
            string? email = null;

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
                // 1) Prefer DB email (source of truth)
                email = await _db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == userId.Value)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync(ct);

                // 2) If DB email missing, allow fallback to request.Email (sent by frontend)
                if (string.IsNullOrWhiteSpace(email))
                {
                    email = request.Email?.Trim();
                }

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

            // 2) Create a reference we control (matching + idempotency)
            var reference = $"LA-{intent.Id}-{Guid.NewGuid():N}"
                .Substring(0, $"LA-{intent.Id}-".Length + 6);

            intent.ProviderReference = reference;
            intent.ProviderTransactionId = null;
            intent.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            // 3) Call Paystack initialize
            try
            {
                // ✅ Callback should be FRONTEND return, not webhook.
                // If not set in env, we fall back to your production frontend return.
                var fallbackFrontendReturn = "https://lawafricadigitalhub.vercel.app/payments/paystack/return";
                var callbackUrl = string.IsNullOrWhiteSpace(_opts.CallbackUrl)
                    ? fallbackFrontendReturn
                    : _opts.CallbackUrl.Trim();

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

        [Authorize]
        [HttpPost("return-visit")]
        public async Task<IActionResult> LogReturnVisit(
            [FromBody] PaystackReturnVisitRequest request,
            CancellationToken ct)
        {
            if (request == null)
                return BadRequest("Invalid request.");

            var reference = (request.Reference ?? "").Trim();
            if (string.IsNullOrWhiteSpace(reference))
                return BadRequest("Reference is required.");

            var userId = HttpContext.User.GetUserId();

            var intent = await _db.PaymentIntents
                .FirstOrDefaultAsync(x =>
                    x.Provider == PaymentProvider.Paystack &&
                    x.ProviderReference == reference &&
                    x.UserId == userId,
                    ct);

            if (intent == null)
                return NotFound("Payment intent not found for this reference.");

            var now = DateTime.UtcNow.ToString("u");
            var line =
                $"[PaystackReturnVisit] {now} " +
                $"url={SafeTrim(request.CurrentUrl ?? "", 240)} " +
                $"ua={SafeTrim(request.UserAgent ?? "", 200)}";

            var existing = intent.AdminNotes ?? "";
            if (!string.IsNullOrWhiteSpace(existing))
                existing += "\n";

            intent.AdminNotes = SafeTrim(existing + line, 500);
            intent.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return Ok(new { ok = true });
        }

        private static string SafeTrim(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }
}
