using LawAfrica.API.Data;
using LawAfrica.API.Models;
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

        public async Task FinalizeIfNeededAsync(int paymentIntentId)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var intent = await _db.PaymentIntents
                .FirstOrDefaultAsync(p => p.Id == paymentIntentId);

            if (intent == null)
                throw new InvalidOperationException("Payment intent not found.");

            if (intent.Status != PaymentStatus.Success)
            {
                await tx.CommitAsync();
                return;
            }

            if (intent.IsFinalized)
            {
                await tx.CommitAsync();
                return;
            }

            // Mark finalized BEFORE domain logic
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
                        ?? intent.ProviderReference
                        ?? "PAYMENT"
                );
            }
            else if (intent.Purpose == PaymentPurpose.PublicProductSubscription)
            {
                // ✅ Public individual subscription activation
                if (intent.UserId == null || intent.ContentProductId == null)
                    throw new InvalidOperationException("Missing UserId or ContentProductId.");

                var months = intent.DurationInMonths ?? 1;
                if (months <= 0) months = 1;

                await CreateOrExtendUserSubscriptionAsync(
                    userId: intent.UserId.Value,
                    contentProductId: intent.ContentProductId.Value,
                    months: months,
                    finalizedByAdminId: intent.ApprovedByUserId
                );
            }
            else if (intent.Purpose == PaymentPurpose.InstitutionProductSubscription)
            {
                if (intent.InstitutionId == null || intent.ContentProductId == null)
                    throw new InvalidOperationException("Missing InstitutionId or ContentProductId.");

                var durationMonths = intent.DurationInMonths ?? 1;

                await _institutionSubscriptionService.CreateOrExtendSubscriptionAsync(
                    intent.InstitutionId.Value,
                    intent.ContentProductId.Value,
                    durationMonths
                );
            }

            await tx.CommitAsync();
        }

        // ✅ Keep as wrapper for manual approvals or future admin tooling
        public async Task FinalizePaymentIntentAsync(int paymentIntentId, int? finalizedByAdminId = null, CancellationToken ct = default)
        {
            var pi = await _db.PaymentIntents.FirstOrDefaultAsync(x => x.Id == paymentIntentId, ct);
            if (pi == null) throw new InvalidOperationException("PaymentIntent not found.");

            if (pi.Status != PaymentStatus.Success) throw new InvalidOperationException("Payment not successful.");
            if (pi.IsFinalized) return;

            // Optional: persist admin id
            if (finalizedByAdminId.HasValue)
            {
                pi.ApprovedByUserId = finalizedByAdminId.Value;
                pi.ApprovedAt = DateTime.UtcNow;
                pi.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            await FinalizeIfNeededAsync(paymentIntentId);
        }

        private async Task CreateOrExtendUserSubscriptionAsync(int userId, int contentProductId, int months, int? finalizedByAdminId)
        {
            var now = DateTime.UtcNow;

            var sub = await _db.UserProductSubscriptions
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ContentProductId == contentProductId);

            if (sub == null)
            {
                sub = new UserProductSubscription
                {
                    UserId = userId,
                    ContentProductId = contentProductId,
                    Status = SubscriptionStatus.Active,
                    StartDate = now,
                    EndDate = now.AddMonths(months),
                    IsTrial = false,
                    GrantedByUserId = finalizedByAdminId
                };
                _db.UserProductSubscriptions.Add(sub);
            }
            else
            {
                sub.Status = SubscriptionStatus.Active;
                sub.IsTrial = false;
                sub.GrantedByUserId = finalizedByAdminId;

                var activeNow = sub.StartDate <= now && sub.EndDate >= now && sub.Status == SubscriptionStatus.Active;

                if (activeNow)
                {
                    sub.EndDate = sub.EndDate.AddMonths(months);
                }
                else
                {
                    sub.StartDate = now;
                    sub.EndDate = now.AddMonths(months);
                }
            }

            await _db.SaveChangesAsync();
        }
    }
}
