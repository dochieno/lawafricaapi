using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Models.Institutions;
using LawAfrica.API.Services.Subscriptions;
using LawAfrica.API.Services.Usage;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Documents
{
    public class DocumentEntitlementService
    {
        private readonly ApplicationDbContext _db;
        private readonly SubscriptionAccessGuard _subscriptionGuard;
        private readonly IUsageEventWriter _usage;

        public DocumentEntitlementService(
            ApplicationDbContext db,
            SubscriptionAccessGuard subscriptionGuard,
            IUsageEventWriter usage)
        {
            _db = db;
            _subscriptionGuard = subscriptionGuard;
            _usage = usage;
        }

        // Existing API - kept
        public async Task<DocumentAccessLevel> GetAccessLevelAsync(int userId, LegalDocument document)
        {
            // Keep this method "pure": it may be called frequently (e.g., lists, previews, library checks).
            // Logging should happen only on intentional "ReaderOpen" or explicit entitlement checks.
            var decision = await GetEntitlementDecisionAsync(userId, document);
            return decision.AccessLevel;
        }

        // (Helpers kept; not used in LINQ to avoid translation errors)
        private static bool CountsAsStudent(InstitutionMemberType t) => t == InstitutionMemberType.Student;

        // Staff bucket includes Staff + InstitutionAdmin (per your previous seat logic)
        private static bool CountsAsStaff(InstitutionMemberType t)
            => t == InstitutionMemberType.Staff || t == InstitutionMemberType.InstitutionAdmin;

        /// <summary>
        /// Logs a usage event for document access, but throttles to avoid floods.
        /// Use this ONLY from the "document opened" path (surface = "ReaderOpen").
        /// </summary>
        private async Task<DocumentEntitlementDecision> LogAndReturnAsync(
            int userId,
            int? institutionId,
            int legalDocumentId,
            DocumentEntitlementDecision decision,
            string surface = "ReaderOpen",
            int throttleWindowSeconds = 60,
            CancellationToken ct = default)
        {
            var allowed = decision.AccessLevel == DocumentAccessLevel.FullAccess;

            // ✅ Null-safe deny reason
            var reason = allowed
                ? "ALLOWED"
                : decision.DenyReason.ToString();

            // ✅ Throttle: skip logging if we've already logged the same event recently
            // This prevents floods caused by repeated calls during scrolling/page loads.
            var nowUtc = DateTime.UtcNow;
            var windowStart = nowUtc.AddSeconds(-Math.Max(5, throttleWindowSeconds));

            var alreadyLoggedRecently = await _db.UsageEvents
                .AsNoTracking()
                .AnyAsync(e =>
                    e.UserId == userId &&
                    e.LegalDocumentId == legalDocumentId &&
                    e.Surface == surface &&
                    e.AtUtc >= windowStart, ct);

            if (!alreadyLoggedRecently)
            {
                await _usage.LogLegalDocumentAccessAsync(
                    legalDocumentId: legalDocumentId,
                    allowed: allowed,
                    reason: reason,
                    surface: surface,
                    userId: userId,
                    institutionId: institutionId
                );
            }

            return decision;
        }



        /// <summary>
        /// Seat enforcement at ACCESS TIME ONLY.
        /// RULE YOU REQUESTED:
        /// - MaxSeats = 0  => block access (no seats allowed)
        /// - MaxSeats = N  => allow up to N, block when used > N
        ///
        /// Seats are counted using memberships:
        /// - Approved AND IsActive == true
        /// </summary>
        private async Task<(bool exceeded, int usedStudents, int maxStudents, int usedStaff, int maxStaff)>
            IsSeatLimitExceededAsync(int institutionId, CancellationToken ct = default)
        {
            var inst = await _db.Institutions
                .AsNoTracking()
                .Where(i => i.Id == institutionId)
                .Select(i => new
                {
                    i.MaxStudentSeats,
                    i.MaxStaffSeats
                })
                .FirstOrDefaultAsync(ct);

            if (inst == null)
                return (false, 0, 0, 0, 0);

            var consuming = _db.InstitutionMemberships
                .AsNoTracking()
                .Where(m =>
                    m.InstitutionId == institutionId &&
                    m.IsActive &&
                    m.Status == MembershipStatus.Approved);

            // ✅ EF-translatable counts
            var usedStudents = await consuming.CountAsync(
                m => m.MemberType == InstitutionMemberType.Student, ct);

            var usedStaff = await consuming.CountAsync(
                m => m.MemberType == InstitutionMemberType.Staff ||
                     m.MemberType == InstitutionMemberType.InstitutionAdmin, ct);

            // ✅ Requested enforcement:
            // 0 means "no seats allowed", N means allow up to N, block when used > N
            bool exceededStudents = usedStudents > inst.MaxStudentSeats;
            bool exceededStaff = usedStaff > inst.MaxStaffSeats;

            bool exceeded = exceededStudents || exceededStaff;

            return (exceeded, usedStudents, inst.MaxStudentSeats, usedStaff, inst.MaxStaffSeats);
        }

        // Additive API - kept
        public async Task<DocumentEntitlementDecision> GetEntitlementDecisionAsync(int userId, LegalDocument document)
        {
            var now = DateTime.UtcNow;

            // Track institutionId when we learn it (for usage analytics)
            int? institutionIdForUsage = null;

            // 1) Free doc => Full
            if (!document.IsPremium)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 2) Load user
            var user = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.UserType, u.InstitutionId })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.PreviewOnly,
                    DenyReason = DocumentEntitlementDenyReason.NotEntitled,
                    Message = "You do not have access to this document.",
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            institutionIdForUsage = user.InstitutionId;

            // 3) Admin => Full
            if (user.UserType == UserType.Admin)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 4) Direct purchase => Full
            var boughtThisDocument = await _db.UserLegalDocumentPurchases
                .AsNoTracking()
                .AnyAsync(p => p.UserId == userId && p.LegalDocumentId == document.Id);

            if (boughtThisDocument)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 5) Determine product(s) covering this doc
            var productIds = await _db.ContentProductLegalDocuments
                .AsNoTracking()
                .Where(x => x.LegalDocumentId == document.Id)
                .Select(x => x.ContentProductId)
                .Distinct()
                .ToListAsync();

            if (document.ContentProductId.HasValue && !productIds.Contains(document.ContentProductId.Value))
                productIds.Add(document.ContentProductId.Value);

            if (productIds.Count == 0)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.PreviewOnly,
                    DenyReason = DocumentEntitlementDenyReason.NotEntitled,
                    Message = "You do not have access to this document.",
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            SubscriptionAccessDecision? institutionDecision = null;

            bool canPurchaseIndividually = true;
            string? purchaseDisabledReason = null;
            bool institutionIsActive = true;

            if (user.InstitutionId.HasValue)
            {
                var inst = await _db.Institutions
                    .AsNoTracking()
                    .Where(i => i.Id == user.InstitutionId.Value)
                    .Select(i => new
                    {
                        i.IsActive,
                        i.AllowIndividualPurchasesWhenInstitutionInactive
                    })
                    .FirstOrDefaultAsync();

                institutionIsActive = inst?.IsActive ?? true;

                // Reuse this flag for BOTH inactive + seat-exceeded block states
                if (!(inst?.AllowIndividualPurchasesWhenInstitutionInactive ?? false))
                {
                    canPurchaseIndividually = false;
                    purchaseDisabledReason =
                        "Purchases are disabled for institution accounts. Please contact your administrator.";
                }

                // Institution inactive => hard lock
                if (!institutionIsActive)
                {
                    var d = new DocumentEntitlementDecision
                    {
                        AccessLevel = DocumentAccessLevel.PreviewOnly,
                        DenyReason = DocumentEntitlementDenyReason.InstitutionSubscriptionInactive,
                        Message = "Your institution is inactive. Please contact your administrator.",

                        CanPurchaseIndividually = canPurchaseIndividually,
                        PurchaseDisabledReason = purchaseDisabledReason
                    };

                    return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
                }

                // ✅ Seat limit enforcement ONLY at access time
                var seat = await IsSeatLimitExceededAsync(user.InstitutionId.Value, ct: default);
                if (seat.exceeded)
                {
                    var d = new DocumentEntitlementDecision
                    {
                        AccessLevel = DocumentAccessLevel.PreviewOnly,
                        DenyReason = DocumentEntitlementDenyReason.InstitutionSeatLimitExceeded,
                        Message =
                            $"Institution seat limit exceeded. Please contact your administrator. " +
                            $"Students: {seat.usedStudents}/{seat.maxStudents}, Staff: {seat.usedStaff}/{seat.maxStaff}.",

                        CanPurchaseIndividually = canPurchaseIndividually,
                        PurchaseDisabledReason = purchaseDisabledReason
                    };

                    return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
                }

                institutionDecision = await _subscriptionGuard.EvaluateInstitutionCoverageAsync(
                    institutionId: user.InstitutionId.Value,
                    productIds: productIds,
                    nowUtc: now,
                    graceDays: 0
                );

                if (institutionDecision.IsInstitutionLock)
                {
                    var d = new DocumentEntitlementDecision
                    {
                        AccessLevel = DocumentAccessLevel.PreviewOnly,
                        DenyReason = DocumentEntitlementDenyReason.InstitutionSubscriptionInactive,
                        Message = institutionDecision.Message
                            ?? "Institution subscription expired. Please contact your administrator.",

                        CanPurchaseIndividually = canPurchaseIndividually,
                        PurchaseDisabledReason = purchaseDisabledReason
                    };

                    return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
                }
            }

            // 4b) Library grants
            var hasLibraryGrant = await _db.UserLibraries
                .AsNoTracking()
                .AnyAsync(l =>
                    l.UserId == userId &&
                    l.LegalDocumentId == document.Id &&
                    (l.AccessType == LibraryAccessType.Purchase ||
                     l.AccessType == LibraryAccessType.Subscription ||
                     l.AccessType == LibraryAccessType.AdminGrant));

            if (hasLibraryGrant)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 6) Institution subscription access
            if (institutionDecision != null && institutionDecision.IsAllowed)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 7) Public ownership
            var ownsAnyContainingProduct = await _db.UserProductOwnerships
                .AsNoTracking()
                .AnyAsync(o =>
                    o.UserId == userId &&
                    productIds.Contains(o.ContentProductId));

            if (ownsAnyContainingProduct)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 8) Public subscription
            var hasPublicSubscription = await _db.UserProductSubscriptions
                .AsNoTracking()
                .AnyAsync(s =>
                    s.UserId == userId &&
                    productIds.Contains(s.ContentProductId) &&
                    s.Status == SubscriptionStatus.Active &&
                    s.StartDate <= now &&
                    s.EndDate >= now);

            if (hasPublicSubscription)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 9) Preview
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.PreviewOnly,
                    DenyReason = DocumentEntitlementDenyReason.NotEntitled,
                    Message = "You do not have access to this document.",

                    CanPurchaseIndividually = canPurchaseIndividually,
                    PurchaseDisabledReason = purchaseDisabledReason
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }
        }
    }
}
