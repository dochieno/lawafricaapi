using LawAfrica.API.Authorization.Policies;
using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Payments;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/payments/governance")]
    [Authorize]
    public class PaymentGovernanceController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly PaymentFinalizerService _finalizer;
        private readonly PaymentValidationService _paymentValidation;

        public PaymentGovernanceController(ApplicationDbContext db, PaymentFinalizerService finalizer,PaymentValidationService paymentValidation)
        {
            _db = db;
            _finalizer = finalizer;
            _paymentValidation = paymentValidation;
        }

        /// <summary>
        /// Creates a manual payment intent for institution subscription (bank transfer, EFT, etc.)
        /// This DOES NOT activate access until approved.
        /// </summary>
        [HttpPost("manual/institution-subscription")]
        public async Task<IActionResult> CreateManualInstitutionSubscriptionPayment(
            [FromBody] CreateManualInstitutionSubscriptionPaymentRequest request)
        {
            try
            {
                await _paymentValidation.ValidateManualInstitutionSubscriptionAsync(
                    request.InstitutionId,
                    request.ContentProductId,
                    request.DurationInMonths,
                    request.Amount,
                    request.ManualReference
                );
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }


            // Create PendingApproval intent
            var intent = new PaymentIntent
            {
                Method = PaymentMethod.BankTransfer,
                Purpose = PaymentPurpose.InstitutionProductSubscription,
                Status = PaymentStatus.PendingApproval,
                InstitutionId = request.InstitutionId,
                ContentProductId = request.ContentProductId,
                // ✅ Add this
                DurationInMonths = request.DurationInMonths,
                Amount = request.Amount,
                Currency = request.Currency,
                ManualReference = request.ManualReference,
                AdminNotes = request.AdminNotes
            };


            _db.PaymentIntents.Add(intent);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Manual payment recorded. Awaiting approval.",
                paymentIntentId = intent.Id,
                status = intent.Status.ToString()
            });
        }

        /// <summary>
        /// Approves a manual payment intent and activates the subscription.
        /// </summary>
            [HttpPost("{paymentIntentId}/approve")]
            public async Task<IActionResult> ApproveManualPayment(int paymentIntentId, [FromBody] ApprovePaymentRequest request)
            {
            var adminUserId = User.GetUserId();

            var intent = await _db.PaymentIntents.FirstOrDefaultAsync(p => p.Id == paymentIntentId);
            if (intent == null)
                return NotFound("Payment intent not found.");

            if (intent.Status != PaymentStatus.PendingApproval)
                return BadRequest("Only PendingApproval payments can be approved.");

            // Mark as successful + approved
            intent.Status = PaymentStatus.Success;
            intent.ApprovedByUserId = adminUserId;
            intent.ApprovedAt = DateTime.UtcNow;
            intent.AdminNotes = request.AdminNotes ?? intent.AdminNotes;
            intent.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Finalize domain action (idempotent)
            await _finalizer.FinalizeIfNeededAsync(intent.Id);

            return Ok(new
            {
                message = "Payment approved and subscription activated.",
                paymentIntentId = intent.Id
            });
        }
    }
}
