using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Documents;
using LawAfrica.API.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/payments/paystack")]
    public class PaystackReturnController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly PaystackService _paystack;
        private readonly PaymentFinalizerService _finalizer;
        private readonly LegalDocumentPurchaseFulfillmentService _legalDocFulfillment;
        private readonly ILogger<PaystackReturnController> _logger;

        public PaystackReturnController(
            ApplicationDbContext db,
            PaystackService paystack,
            PaymentFinalizerService finalizer,
            LegalDocumentPurchaseFulfillmentService legalDocFulfillment,
            ILogger<PaystackReturnController> logger)
        {
            _db = db;
            _paystack = paystack;
            _finalizer = finalizer;
            _legalDocFulfillment = legalDocFulfillment;
            _logger = logger;
        }

        // ✅ This prevents the 405 if the frontend/browser hits it as GET
        [Authorize]
        [HttpGet("return-visit")]
        public IActionResult ReturnVisitGet([FromQuery] string? reference)
        {
            _logger.LogInformation("[PAYSTACK RETURN VISIT][GET] ref={Ref} user={UserId}",
                reference, User?.Identity?.Name ?? "unknown");
            return Ok(new { ok = true });
        }

        public class ReturnVisitRequest
        {
            public string Reference { get; set; } = string.Empty;
        }

        // ✅ If your frontend calls it as POST, this works too
        [AllowAnonymous]
        [HttpPost("return-visit")]
        public IActionResult ReturnVisitPost([FromBody] ReturnVisitRequest req)
        {
            _logger.LogInformation("[PAYSTACK RETURN VISIT][POST] ref={Ref} user={UserId}",
                req?.Reference, User?.Identity?.Name ?? "unknown");
            return Ok(new { ok = true });
        }

        public class ConfirmPaystackRequest
        {
            public string Reference { get; set; } = string.Empty;
        }

        // ✅ Optional but HIGHLY recommended:
        // If webhook is delayed, the return page can "confirm" payment server-side and proceed.
        [AllowAnonymous]
        [HttpPost("confirm")]
        public async Task<IActionResult> Confirm([FromBody] ConfirmPaystackRequest req, CancellationToken ct)
        {
            var reference = (req?.Reference ?? "").Trim();
            if (string.IsNullOrWhiteSpace(reference))
                return BadRequest("Reference is required.");

            var intent = await _db.PaymentIntents
                .FirstOrDefaultAsync(x =>
                    x.Provider == PaymentProvider.Paystack &&
                    x.ProviderReference == reference, ct);

            if (intent == null)
                return NotFound("PaymentIntent not found.");

            // ✅ If the caller IS authenticated, enforce ownership (prevents hijacking)
            if (User?.Identity?.IsAuthenticated == true)
            {
                var userId = User.GetUserId();
                if (intent.UserId.HasValue && intent.UserId.Value != userId)
                    return Forbid();
            }

            // ✅ If already success, finalize+fulfill idempotently
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

            // Otherwise verify now (server-to-server truth)
            var verify = await _paystack.VerifyTransactionAsync(reference, ct);

            if (!verify.IsSuccessful)
                return BadRequest($"Paystack verify not successful: {verify.Status}");

            // Validate amount/currency
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

            // Fulfill legal doc purchase
            if (intent.Purpose == PaymentPurpose.PublicLegalDocumentPurchase)
                await _legalDocFulfillment.FulfillAsync(intent);

            // Finalize
            await _finalizer.FinalizeIfNeededAsync(intent.Id);

            return Ok(new
            {
                ok = true,
                status = intent.Status.ToString(),
                paymentIntentId = intent.Id,
                legalDocumentId = intent.LegalDocumentId
            });
        }
    }
}
