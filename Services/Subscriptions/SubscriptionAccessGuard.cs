using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Subscriptions
{
    /// <summary>
    /// Central policy guard for subscription lifecycle enforcement.
    /// Controllers/services call this to avoid scattering status/date checks everywhere.
    ///
    /// Key behaviors:
    /// - Institution must be active, otherwise institution access is not applicable.
    /// - Personal purchase still overrides everything (handled by caller).
    /// - If an institution has a subscription covering a product but it is inactive (Expired/Suspended/NotStarted),
    ///   the caller may choose to HARD-BLOCK access even if the user has a library grant.
    /// </summary>
    public class SubscriptionAccessGuard
    {
        private readonly ApplicationDbContext _db;

        public SubscriptionAccessGuard(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Evaluate institution subscription coverage for a set of productIds.
        ///
        /// Returns:
        /// - Allowed: at least one covering subscription is ACTIVE and within date window (or grace).
        /// - IsInstitutionLock: there exists a covering subscription but it is inactive (Expired/Suspended/NotStarted),
        ///   meaning institution-managed access should be blocked for covered documents (even library grants),
        ///   depending on caller rules.
        ///
        /// If the institution has NO subscription row for those products, IsInstitutionLock = false.
        /// </summary>
        public async Task<SubscriptionAccessDecision> EvaluateInstitutionCoverageAsync(
            int institutionId,
            IReadOnlyCollection<int> productIds,
            DateTime nowUtc,
            int graceDays = 0,
            CancellationToken ct = default)
        {
            if (institutionId <= 0)
            {
                return SubscriptionAccessDecision.Deny(SubscriptionAccessDenyReason.NoInstitution);
            }

            if (productIds == null || productIds.Count == 0)
            {
                return SubscriptionAccessDecision.Deny(SubscriptionAccessDenyReason.NoProducts);
            }

            // 1) Institution active?
            var institutionIsActive = await _db.Institutions
                .AsNoTracking()
                .Where(i => i.Id == institutionId)
                .Select(i => i.IsActive)
                .FirstOrDefaultAsync(ct); // false when not found => safe

            if (!institutionIsActive)
            {
                return SubscriptionAccessDecision.Deny(
                    SubscriptionAccessDenyReason.InstitutionInactive,
                    message: "Institution is inactive."
                );
            }

            // 2) Pull only the relevant subscriptions
            var subs = await _db.InstitutionProductSubscriptions
                .AsNoTracking()
                .Where(s => s.InstitutionId == institutionId && productIds.Contains(s.ContentProductId))
                .Select(s => new
                {
                    s.Id,
                    s.ContentProductId,
                    s.Status,
                    s.StartDate,
                    s.EndDate
                })
                .ToListAsync(ct);

            if (subs.Count == 0)
            {
                // No subscription relationship for these products => no lock from institution policy
                return SubscriptionAccessDecision.Deny(SubscriptionAccessDenyReason.NoSubscriptionRow);
            }

            // Grace window (if you later want it)
            var graceEnd = graceDays > 0 ? nowUtc.AddDays(-graceDays) : nowUtc;

            // 3) Determine any ACTIVE coverage
            bool anyActiveCover =
                subs.Any(s =>
                    s.Status == SubscriptionStatus.Active &&
                    s.StartDate <= nowUtc &&
                    s.EndDate >= nowUtc);

            if (anyActiveCover)
            {
                return SubscriptionAccessDecision.Allow(
                    message: "Institution subscription active."
                );
            }

            // 4) Determine lock reasons (institution has a subscription row but not granting)
            // If ANY covering sub exists and is inactive, caller may choose to hard-block.
            // Order of precedence for reason:
            // Suspended > Expired > NotStarted > InactiveDates/Unknown
            if (subs.Any(s => s.Status == SubscriptionStatus.Suspended))
            {
                return SubscriptionAccessDecision.Lock(
                    SubscriptionAccessDenyReason.Suspended,
                    "Institution subscription expired. Please contact your administrator."
                );
            }

            // Expired by status OR expired by end date
            if (subs.Any(s => s.Status == SubscriptionStatus.Expired || s.EndDate < graceEnd))
            {
                return SubscriptionAccessDecision.Lock(
                    SubscriptionAccessDenyReason.Expired,
                    "Institution subscription expired. Please contact your administrator."
                );
            }

            // Not started yet
            if (subs.Any(s => s.StartDate > nowUtc || s.Status == SubscriptionStatus.Pending))
            {
                return SubscriptionAccessDecision.Lock(
                    SubscriptionAccessDenyReason.NotStarted,
                    "Institution subscription is not active yet. Please contact your administrator."
                );
            }

            // Fallback: there is a subscription row but it isn't granting access
            return SubscriptionAccessDecision.Lock(
                SubscriptionAccessDenyReason.NotEntitled,
                "Institution subscription is not active. Please contact your administrator."
            );
        }
    }
}
