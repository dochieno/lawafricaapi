using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;
namespace LawAfrica.API.Services
{
    /// <summary>
    /// Handles creation of permanent content ownership
    /// after successful public payment.
    /// </summary>
    public class PurchaseService
    {
        private readonly ApplicationDbContext _db;

        public PurchaseService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<UserProductOwnership> CompletePublicPurchaseAsync(
            int userId,
            int contentProductId,
            string transactionReference)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null)
                throw new InvalidOperationException("User not found.");

            // 🔒 Only public users can purchase individually
            if (user.UserType != UserType.Public)
                throw new InvalidOperationException("Only public users can purchase content.");

            var product = await _db.ContentProducts.FindAsync(contentProductId);
            if (product == null)
                throw new InvalidOperationException("Product not found.");

            if (product.AccessModel != ProductAccessModel.OneTimePurchase)
                throw new InvalidOperationException("This product is not purchasable.");

            // 🔁 Idempotency: prevent duplicate ownership
            var existingOwnership = await _db.UserProductOwnerships
                .FirstOrDefaultAsync(o =>
                    o.UserId == userId &&
                    o.ContentProductId == contentProductId);

            if (existingOwnership != null)
                return existingOwnership;

            var ownership = new UserProductOwnership
            {
                UserId = userId,
                ContentProductId = contentProductId,
                TransactionReference = transactionReference,
                PurchasedAt = DateTime.UtcNow
            };

            _db.UserProductOwnerships.Add(ownership);
            await _db.SaveChangesAsync();

            return ownership;
        }
    }
}
