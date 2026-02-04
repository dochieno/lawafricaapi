using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Subscriptions
{
    /// <summary>
    /// Central policy guard for institution subscription lifecycle enforcement.
    ///
    /// Returns:
    /// - Allowed: at least one covering subscription is ACTIVE and within date window (or grace).
    /// - Lock: there exists at least one subscription row for the product(s), but not granting right now
    ///         (Expired / Suspended / NotStarted etc). Caller may choose to hard-block.
    /// - Deny: no relationship rows / invalid inputs / institution inactive etc.
    /// </summary>
    public class SubscriptionAccessGuard
    {
        private readonly ApplicationDbContext _db;

        public SubscriptionAccessGuard(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<SubscriptionAccessDecision> EvaluateInstitutionCoverageAsync(
            int institutionId,
            IReadOnlyCollection<int> productIds,
            DateTime nowUtc,
            int graceDays = 0,
            CancellationToken ct = default)
        {
            if (institutionId <= 0)
                return SubscriptionAccessDecision.Deny(SubscriptionAccessDenyReason.NoInstitution);

            if (productIds == null || productIds.Count == 0)
                return SubscriptionAccessDecision.Deny(SubscriptionAccessDenyReason.NoProducts);

            // Clamp grace to safe range
            if (graceDays < 0) graceDays = 0;
            if (graceDays > 365) graceDays = 365;

            // ✅ Grace cutoff: treat subscription as still valid if EndDate >= (now - graceDays)
            // (Still must have started)
            var graceCutoff = graceDays > 0 ? nowUtc.AddDays(-graceDays) : nowUtc;

            // 1) Institution active?
            var institutionIsActive = await _db.Institutions
                .AsNoTracking()
                .Where(i => i.Id == institutionId)
                .Select(i => i.IsActive)
                .FirstOrDefaultAsync(ct); // false when not found => safe deny

            if (!institutionIsActive)
            {
                return SubscriptionAccessDecision.Deny(
                    SubscriptionAccessDenyReason.InstitutionInactive,
                    message: "Institution is inactive."
                );
            }

            // 2) Load only relevant subscriptions
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
                // No institution subscription relationship for these products => no lock (caller may allow other unlock paths)
                return SubscriptionAccessDecision.Deny(SubscriptionAccessDenyReason.NoSubscriptionRow);
            }

            // 3) Any ACTIVE coverage? (✅ apply graceCutoff)
            // Must have started (StartDate <= nowUtc)
            // EndDate >= graceCutoff allows grace window.
            bool anyActiveCover = subs.Any(s =>
                s.Status == SubscriptionStatus.Active &&
                s.StartDate <= nowUtc &&
                s.EndDate >= graceCutoff);

            if (anyActiveCover)
            {
                return SubscriptionAccessDecision.Allow("Institution subscription active.");
            }

            // 4) Institution lock reasons (there IS a row, but it isn't granting now)
            // Precedence: Suspended > Expired > NotStarted > NotEntitled

            if (subs.Any(s => s.Status == SubscriptionStatus.Suspended))
            {
                return SubscriptionAccessDecision.Lock(
                    SubscriptionAccessDenyReason.Suspended,
                    "Institution subscription is suspended. Please contact your administrator."
                );
            }

            // Expired by status OR by end-date (consider grace)
            if (subs.Any(s =>
                    s.Status == SubscriptionStatus.Expired ||
                    s.EndDate < graceCutoff))
            {
                return SubscriptionAccessDecision.Lock(
                    SubscriptionAccessDenyReason.Expired,
                    "Institution subscription expired. Please contact your administrator."
                );
            }

            // Not started yet (Pending OR start in future)
            if (subs.Any(s =>
                    s.Status == SubscriptionStatus.Pending ||
                    s.StartDate > nowUtc))
            {
                return SubscriptionAccessDecision.Lock(
                    SubscriptionAccessDenyReason.NotStarted,
                    "Institution subscription is not active yet. Please contact your administrator."
                );
            }

            // Fallback: there is a subscription row, but it isn't granting access
            return SubscriptionAccessDecision.Lock(
                SubscriptionAccessDenyReason.NotEntitled,
                "Institution subscription is not active. Please contact your administrator."
            );
        }
    }
}
