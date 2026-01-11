using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services
{
    public class ContentEntitlementService
    {
        private readonly ApplicationDbContext _db;

        // Keep in sync with your frontend + seed endpoint
        private const string BUNDLE_PRODUCT_NAME = "Institution All-Access Bundle";

        public ContentEntitlementService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<bool> HasAccess(User user, ContentProduct product)
        {
            if (user == null) return false;
            if (product == null) return false;

            var now = DateTime.UtcNow;

            // ✅ Global admin
            if (user.UserType == UserType.Admin)
                return true;

            // =========================
            // INSTITUTION AUDIENCE PATH
            // =========================
            if (user.InstitutionId.HasValue)
            {
                // If product not available to institutions, deny
                if (!product.AvailableToInstitutions)
                    return false;

                // Determine institution model, with legacy fallback
                var instModel = product.InstitutionAccessModel != ProductAccessModel.Unknown
                    ? product.InstitutionAccessModel
                    : product.AccessModel; // legacy fallback

                // ✅ Institution subscription access
                if (instModel == ProductAccessModel.Subscription)
                {
                    // 1) Direct subscription to the product
                    var hasDirect = await _db.InstitutionProductSubscriptions.AnyAsync(s =>
                        s.InstitutionId == user.InstitutionId.Value &&
                        s.ContentProductId == product.Id &&
                        s.Status == SubscriptionStatus.Active &&
                        s.StartDate <= now &&
                        s.EndDate > now
                    );

                    if (hasDirect)
                        return true;

                    // 2) Bundle subscription grants access only if product is included in bundle
                    if (product.IncludedInInstitutionBundle)
                    {
                        // Find the bundle product id
                        var bundleProductId = await _db.ContentProducts
                            .AsNoTracking()
                            .Where(p => p.Name == BUNDLE_PRODUCT_NAME)
                            .Select(p => (int?)p.Id)
                            .FirstOrDefaultAsync();

                        if (bundleProductId.HasValue)
                        {
                            var hasBundle = await _db.InstitutionProductSubscriptions.AnyAsync(s =>
                                s.InstitutionId == user.InstitutionId.Value &&
                                s.ContentProductId == bundleProductId.Value &&
                                s.Status == SubscriptionStatus.Active &&
                                s.StartDate <= now &&
                                s.EndDate > now
                            );

                            if (hasBundle)
                                return true;
                        }
                    }

                    return false;
                }

                // If you later support institution "one-time purchase", add it here.
                // For now, deny for institutions if not subscription-based.
                return false;
            }

            // ====================
            // PUBLIC USER PATH
            // ====================
            if (!product.AvailableToPublic)
                return false;

            // Determine public model, with legacy fallback
            var publicModel = product.PublicAccessModel != ProductAccessModel.Unknown
                ? product.PublicAccessModel
                : product.AccessModel; // legacy fallback

            // One-time ownership (public)
            if (publicModel == ProductAccessModel.OneTimePurchase)
            {
                return await _db.UserProductOwnerships.AnyAsync(o =>
                    o.UserId == user.Id &&
                    o.ContentProductId == product.Id
                );
            }

            // Subscription (public)
            if (publicModel == ProductAccessModel.Subscription)
            {
                return await _db.UserProductSubscriptions.AnyAsync(s =>
                    s.UserId == user.Id &&
                    s.ContentProductId == product.Id &&
                    s.Status == SubscriptionStatus.Active &&
                    s.StartDate <= now &&
                    s.EndDate > now
                );
            }

            return false;
        }
    }
}
