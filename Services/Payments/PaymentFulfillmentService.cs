using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Payments;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Payments
{
    /// <summary>
    /// Runs the business action once a PaymentIntent is confirmed paid.
    /// Idempotent because callbacks may repeat.
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
                case PaymentPurpose.PublicLegalDocumentPurchase:
                    await FulfillPublicLegalDocumentPurchase(intent);
                    break;

                default:
                    // Other purposes handled elsewhere / later
                    break;
            }

            // Mark finalized so we don't re-run on duplicate callbacks
            intent.IsFinalized = true;
            intent.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        private async Task FulfillPublicLegalDocumentPurchase(PaymentIntent intent)
        {
            if (!intent.UserId.HasValue)
                throw new InvalidOperationException("Legal document purchase requires PaymentIntent.UserId.");

            if (!intent.LegalDocumentId.HasValue)
                throw new InvalidOperationException("Legal document purchase requires PaymentIntent.LegalDocumentId.");

            var userId = intent.UserId.Value;
            var docId = intent.LegalDocumentId.Value;

            // 1) Create purchase record (entitlement) idempotently
            var alreadyPurchased = await _db.UserLegalDocumentPurchases
                .AsNoTracking()
                .AnyAsync(x => x.UserId == userId && x.LegalDocumentId == docId);

            if (!alreadyPurchased)
            {
                _db.UserLegalDocumentPurchases.Add(new UserLegalDocumentPurchase
                {
                    UserId = userId,
                    LegalDocumentId = docId,
                    PurchasedAt = DateTime.UtcNow,

                    // metadata from PaymentIntent
                    Amount = intent.Amount,
                    Currency = string.IsNullOrWhiteSpace(intent.Currency) ? "KES" : intent.Currency,
                    PaymentReference = intent.MpesaReceiptNumber ?? intent.ManualReference
                });
            }

            // 2) Add to library automatically (UX) idempotently
            var alreadyInLibrary = await _db.UserLibraries
                .AsNoTracking()
                .AnyAsync(x => x.UserId == userId && x.LegalDocumentId == docId);

            if (!alreadyInLibrary)
            {
                _db.UserLibraries.Add(new UserLibrary
                {
                    UserId = userId,
                    LegalDocumentId = docId,
                    AccessType = LibraryAccessType.Purchase,
                    AddedAt = DateTime.UtcNow
                });
            }
        }
    }
}
