using LawAfrica.API.Data;
using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Services.Payments
{
    /// <summary>
    /// Runs business actions once a PaymentIntent is confirmed paid.
    /// IMPORTANT:
    /// - PublicLegalDocumentPurchase is fulfilled by LegalDocumentPurchaseFulfillmentService (single source of truth),
    ///   which also sets IsFinalized.
    /// - This service can still finalize other payment purposes in future.
    /// </summary>
    public class PaymentFulfillmentService
    {
        private readonly ApplicationDbContext _db;

        public PaymentFulfillmentService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task FulfillAsync(PaymentIntent intent)
        {
            // Only fulfill successful intents
            if (intent.Status != PaymentStatus.Success) return;

            // Prevent double fulfillment
            if (intent.IsFinalized) return;

            switch (intent.Purpose)
            {
                // ✅ Legal doc purchases are fulfilled by LegalDocumentPurchaseFulfillmentService
                // to keep behavior consistent with MPesa callback + healing service.
                case PaymentPurpose.PublicLegalDocumentPurchase:
                    return;

                default:
                    // Other purposes handled elsewhere / later
                    break;
            }

            // Mark finalized so we don't re-run on duplicate callbacks (for the purposes handled here)
            intent.IsFinalized = true;
            intent.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }
    }
}