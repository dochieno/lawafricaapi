using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Payments;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Payments
{
    /// <summary>
    /// Fulfillment for PublicLegalDocumentPurchase.
    /// Runs after Mpesa confirms payment success.
    /// Idempotent: safe on duplicate callbacks.
    /// </summary>
    public class LegalDocumentPurchaseFulfillmentService
    {
        private readonly ApplicationDbContext _db;

        public LegalDocumentPurchaseFulfillmentService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task FulfillAsync(PaymentIntent intent)
        {
            if (intent.Purpose != PaymentPurpose.PublicLegalDocumentPurchase)
                return;

            if (intent.Status != PaymentStatus.Success)
                return;

            // ✅ Idempotency guard (important: Mpesa can retry callbacks)
            if (intent.IsFinalized)
                return;

            if (!intent.UserId.HasValue)
                throw new InvalidOperationException("PaymentIntent.UserId is required for legal document purchase.");

            if (!intent.LegalDocumentId.HasValue)
                throw new InvalidOperationException("PaymentIntent.LegalDocumentId is required for legal document purchase.");

            var userId = intent.UserId.Value;
            var docId = intent.LegalDocumentId.Value;

            // ✅ Transaction ensures we don't partially apply (purchase without library, etc.)
            await using var tx = await _db.Database.BeginTransactionAsync();

            // 1) Purchase record (entitlement)
            var alreadyPurchased = await _db.UserLegalDocumentPurchases
                .AsNoTracking()
                .AnyAsync(p => p.UserId == userId && p.LegalDocumentId == docId);

            if (!alreadyPurchased)
            {
                _db.UserLegalDocumentPurchases.Add(new UserLegalDocumentPurchase
                {
                    UserId = userId,
                    LegalDocumentId = docId,
                    PurchasedAt = DateTime.UtcNow,
                    Amount = intent.Amount,
                    Currency = string.IsNullOrWhiteSpace(intent.Currency) ? "KES" : intent.Currency,
                    PaymentReference = intent.MpesaReceiptNumber ?? intent.ManualReference
                });
            }

            // 2) Auto-add to library for convenience
            var alreadyInLibrary = await _db.UserLibraries
                .AsNoTracking()
                .AnyAsync(l => l.UserId == userId && l.LegalDocumentId == docId);

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

            // 3) Mark the payment intent finalized (critical)
            intent.IsFinalized = true;
            intent.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
    }
}
