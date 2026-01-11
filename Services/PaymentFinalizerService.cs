using LawAfrica.API.Data;
using LawAfrica.API.Models.Payments;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services
{
    public class PaymentFinalizerService
    {
        private readonly ApplicationDbContext _db;
        private readonly RegistrationService _registrationService;
        private readonly PurchaseService _purchaseService;
        private readonly InstitutionSubscriptionService _institutionSubscriptionService;

        public PaymentFinalizerService(
            ApplicationDbContext db,
            RegistrationService registrationService,
            PurchaseService purchaseService,
            InstitutionSubscriptionService institutionSubscriptionService)
        {
            _db = db;
            _registrationService = registrationService;
            _purchaseService = purchaseService;
            _institutionSubscriptionService = institutionSubscriptionService;
        }

        /// <summary>
        /// Finalizes domain action once the PaymentIntent is in Success state.
        /// Safe for retries (callback can be sent multiple times).
        /// </summary>
        public async Task FinalizeIfNeededAsync(int paymentIntentId)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var intent = await _db.PaymentIntents
                .FirstOrDefaultAsync(p => p.Id == paymentIntentId);

            if (intent == null)
                throw new InvalidOperationException("Payment intent not found.");

            // Only finalize if successful
            if (intent.Status != PaymentStatus.Success)
            {
                await tx.CommitAsync();
                return;
            }

            // Idempotency: do nothing if already finalized
            if (intent.IsFinalized)
            {
                await tx.CommitAsync();
                return;
            }

            // Mark finalized BEFORE executing domain logic (prevents concurrent double execution)
            intent.IsFinalized = true;
            intent.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Execute domain action depending on purpose
            if (intent.Purpose == PaymentPurpose.PublicSignupFee)
            {
                if (intent.RegistrationIntentId == null)
                    throw new InvalidOperationException("Missing RegistrationIntentId.");

                var regIntent = await _db.RegistrationIntents
                    .FirstOrDefaultAsync(r => r.Id == intent.RegistrationIntentId.Value);

                if (regIntent == null)
                    throw new InvalidOperationException("Registration intent not found.");

                regIntent.PaymentCompleted = true;
                await _db.SaveChangesAsync();

                await _registrationService.CreateUserFromIntentAsync(regIntent);
            }
            else if (intent.Purpose == PaymentPurpose.PublicProductPurchase)
            {
                if (intent.UserId == null || intent.ContentProductId == null)
                    throw new InvalidOperationException("Missing UserId or ContentProductId.");

                await _purchaseService.CompletePublicPurchaseAsync(
                    intent.UserId.Value,
                    intent.ContentProductId.Value,
                    intent.MpesaReceiptNumber
                        ?? intent.ManualReference
                        ?? intent.CheckoutRequestId
                        ?? "PAYMENT"
                );
            }
            else if (intent.Purpose == PaymentPurpose.InstitutionProductSubscription)
            {
                if (intent.InstitutionId == null || intent.ContentProductId == null)
                    throw new InvalidOperationException("Missing InstitutionId or ContentProductId.");

                // Recommended: store duration in PaymentIntent later as MetadataJson.
                // For now, set a sensible default (or add DurationInMonths column in Phase 2.4.1).
                var durationMonths = intent.DurationInMonths ?? 1;
                await _institutionSubscriptionService.CreateOrExtendSubscriptionAsync(
                    intent.InstitutionId.Value,
                    intent.ContentProductId.Value,
                    durationMonths
                );
            }

            await tx.CommitAsync();
        }
    }
}
