using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services
{
    /// <summary>
    /// Centralized institution access rules:
    /// - Direct subscription to product grants access if valid now.
    /// - Bundle subscription grants access ONLY if product.IncludedInInstitutionBundle == true.
    /// - Excluded products (IncludedInInstitutionBundle=false) require separate subscription even if bundle is active.
    /// - Suspended never grants access.
    /// Defense-in-depth: checks dates as well as status.
    /// </summary>
    public class InstitutionAccessService
    {
        private readonly ApplicationDbContext _db;

        public const string BUNDLE_PRODUCT_NAME = "Institution All-Access Bundle";

        public InstitutionAccessService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<InstitutionProductAccessResult> CheckInstitutionProductAccessAsync(
            int institutionId,
            int contentProductId,
            DateTime? asOfUtc = null)
        {
            var now = asOfUtc ?? DateTime.UtcNow;

            var institutionExists = await _db.Institutions.AsNoTracking().AnyAsync(i => i.Id == institutionId);
            if (!institutionExists)
                return InstitutionProductAccessResult.Deny(now, "Institution not found.");

            var product = await _db.ContentProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == contentProductId);
            if (product == null)
                return InstitutionProductAccessResult.Deny(now, "Product not found.");

            if (!product.AvailableToInstitutions)
                return InstitutionProductAccessResult.Deny(now, "Product not available to institutions.");

            // Institutions must use InstitutionAccessModel (fallback to legacy AccessModel if ever needed)
            var instModel = product.InstitutionAccessModel != ProductAccessModel.Unknown
                ? product.InstitutionAccessModel
                : product.AccessModel;

            if (instModel != ProductAccessModel.Subscription)
                return InstitutionProductAccessResult.Deny(now, "Product is not subscription-based for institutions.");

            // 1) Direct subscription always qualifies (if valid)
            var direct = await _db.InstitutionProductSubscriptions
                .AsNoTracking()
                .Where(s => s.InstitutionId == institutionId && s.ContentProductId == contentProductId)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            if (direct != null && IsValidNow(direct, now))
            {
                return new InstitutionProductAccessResult
                {
                    HasAccess = true,
                    AsOfUtc = now,
                    ViaDirectSubscription = true,
                    DirectSubscriptionId = direct.Id,
                    Reason = "Access granted via direct subscription."
                };
            }

            // 2) Bundle only qualifies if product is included in bundle
            if (!product.IncludedInInstitutionBundle)
            {
                return InstitutionProductAccessResult.Deny(now,
                    "Product is excluded from institution bundle and requires a separate subscription.");
            }

            // Find bundle product id (by name)
            var bundleProductId = await _db.ContentProducts
                .AsNoTracking()
                .Where(p => p.Name == BUNDLE_PRODUCT_NAME)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync();

            if (!bundleProductId.HasValue)
            {
                return InstitutionProductAccessResult.Deny(now,
                    "Bundle product not configured (Institution All-Access Bundle).");
            }

            var bundleSub = await _db.InstitutionProductSubscriptions
                .AsNoTracking()
                .Where(s => s.InstitutionId == institutionId && s.ContentProductId == bundleProductId.Value)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            if (bundleSub != null && IsValidNow(bundleSub, now))
            {
                return new InstitutionProductAccessResult
                {
                    HasAccess = true,
                    AsOfUtc = now,
                    ViaBundle = true,
                    BundleSubscriptionId = bundleSub.Id,
                    Reason = "Access granted via institution bundle."
                };
            }

            // Otherwise denied
            return InstitutionProductAccessResult.Deny(now, "No valid subscription found (direct or bundle).");
        }

        private static bool IsValidNow(InstitutionProductSubscription s, DateTime nowUtc)
        {
            // Never grant if suspended
            if (s.Status == SubscriptionStatus.Suspended) return false;

            // Defense in depth: status should be Active, AND dates must be in-range.
            if (s.Status != SubscriptionStatus.Active) return false;

            return s.StartDate <= nowUtc && s.EndDate > nowUtc;
        }
    }

    public class InstitutionProductAccessResult
    {
        public bool HasAccess { get; set; }
        public DateTime AsOfUtc { get; set; }

        public bool ViaBundle { get; set; }
        public int? BundleSubscriptionId { get; set; }

        public bool ViaDirectSubscription { get; set; }
        public int? DirectSubscriptionId { get; set; }

        public string Reason { get; set; } = string.Empty;

        public static InstitutionProductAccessResult Deny(DateTime now, string reason) => new InstitutionProductAccessResult
        {
            HasAccess = false,
            AsOfUtc = now,
            Reason = reason
        };
    }
}
