using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Models.Institutions;
using LawAfrica.API.Models.LawReports.Enums;
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

        /// <summary>
        /// Lightweight wrapper that returns only AccessLevel.
        /// </summary>
        public async Task<DocumentAccessLevel> GetAccessLevelAsync(int userId, LegalDocument document)
        {
            var decision = await GetEntitlementDecisionAsync(userId, document);
            return decision.AccessLevel;
        }

        // Helpers retained (and explicitly not used inside LINQ) to avoid EF translation issues.
        private static bool CountsAsStudent(InstitutionMemberType t) => t == InstitutionMemberType.Student;

        // Staff bucket includes Staff + InstitutionAdmin (per your seat logic).
        private static bool CountsAsStaff(InstitutionMemberType t)
            => t == InstitutionMemberType.Staff || t == InstitutionMemberType.InstitutionAdmin;

        /// <summary>
        /// Usage analytics logger with throttling.
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

            // If allowed => reason is ALLOWED; otherwise use deny reason.
            var reason = allowed ? "ALLOWED" : decision.DenyReason.ToString();

            // Throttle: if same user opened same doc on same surface recently, skip logging.
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

            var usedStudents = await consuming.CountAsync(
                m => m.MemberType == InstitutionMemberType.Student, ct);

            var usedStaff = await consuming.CountAsync(
                m => m.MemberType == InstitutionMemberType.Staff ||
                     m.MemberType == InstitutionMemberType.InstitutionAdmin, ct);

            bool exceededStudents = usedStudents > inst.MaxStudentSeats;
            bool exceededStaff = usedStaff > inst.MaxStaffSeats;

            bool exceeded = exceededStudents || exceededStaff;

            return (exceeded, usedStudents, inst.MaxStudentSeats, usedStaff, inst.MaxStaffSeats);
        }

        // =========================================================
        // ✅ REPORTS GRACE/TRIAL DAYS (GlobalAdmin-controlled)
        //
        // This MUST compile without changing DI.
        // Replace this implementation later to read from your GlobalAdmin settings store.
        // =========================================================
        private Task<int> GetReportGraceDaysAsync(CancellationToken ct = default)
        {
            // Suggested temporary source: env var set on server (Render/Neon deploy)
            // GlobalAdmin UI can later write to DB settings; then change this method only.
            var raw = Environment.GetEnvironmentVariable("REPORT_GRACE_DAYS");
            if (int.TryParse(raw, out var days))
            {
                if (days < 0) days = 0;
                if (days > 365) days = 365;
                return Task.FromResult(days);
            }

            return Task.FromResult(0);
        }

        // =========================================================
        // ✅ REPORTS: subscription-only entitlement
        //
        // RULES:
        // - Ignore: document purchases, product ownership (lifetime), library grants.
        // - Allow if:
        //    (A) user has active subscription to ANY containing product (independent), OR
        //    (B) institution has active subscription to ANY containing product
        // - Grace/trial: allow within N days after EndDate (N = GlobalAdmin)
        // - IMPORTANT: institution inactive/seat limits must NOT block personal subscription
        // =========================================================
        private async Task<DocumentEntitlementDecision> GetReportEntitlementDecisionAsync(
            int userId,
            int? institutionId,
            LegalDocument document,
            List<int> productIds,
            DateTime nowUtc,
            CancellationToken ct = default)
        {
            var graceDays = await GetReportGraceDaysAsync(ct);
            var graceCutoff = nowUtc.AddDays(-graceDays);

            // (A) Personal subscription first (independent)
            var hasUserSubscription = await _db.UserProductSubscriptions
                .AsNoTracking()
                .AnyAsync(s =>
                    s.UserId == userId &&
                    productIds.Contains(s.ContentProductId) &&
                    s.Status == SubscriptionStatus.Active &&
                    s.StartDate <= nowUtc &&
                    s.EndDate >= graceCutoff, ct);

            if (hasUserSubscription)
            {
                return new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                };
            }

            // (B) Institution subscription path (only if linked to institution)
            if (institutionId.HasValue)
            {
                // Institution inactive -> block institution path only
                var instActive = await _db.Institutions
                    .AsNoTracking()
                    .Where(i => i.Id == institutionId.Value)
                    .Select(i => i.IsActive)
                    .FirstOrDefaultAsync(ct);

                if (!instActive)
                {
                    return new DocumentEntitlementDecision
                    {
                        AccessLevel = DocumentAccessLevel.PreviewOnly,
                        DenyReason = DocumentEntitlementDenyReason.InstitutionSubscriptionInactive,
                        Message = "Your institution is inactive. Please contact your administrator.",
                        CanPurchaseIndividually = true,
                        PurchaseDisabledReason = null
                    };
                }

                // Seat limits apply only for institution access (personal already checked above)
                var seat = await IsSeatLimitExceededAsync(institutionId.Value, ct);
                if (seat.exceeded)
                {
                    return new DocumentEntitlementDecision
                    {
                        AccessLevel = DocumentAccessLevel.PreviewOnly,
                        DenyReason = DocumentEntitlementDenyReason.InstitutionSeatLimitExceeded,
                        Message =
                            $"Institution seat limit exceeded. Please contact your administrator. " +
                            $"Students: {seat.usedStudents}/{seat.maxStudents}, Staff: {seat.usedStaff}/{seat.maxStaff}.",
                        CanPurchaseIndividually = true,
                        PurchaseDisabledReason = null
                    };
                }

                // Use your existing guard; pass graceDays for reports
                var institutionDecision = await _subscriptionGuard.EvaluateInstitutionCoverageAsync(
                    institutionId: institutionId.Value,
                    productIds: productIds,
                    nowUtc: nowUtc,
                    graceDays: graceDays
                );

                if (institutionDecision.IsInstitutionLock)
                {
                    return new DocumentEntitlementDecision
                    {
                        AccessLevel = DocumentAccessLevel.PreviewOnly,
                        DenyReason = DocumentEntitlementDenyReason.InstitutionSubscriptionInactive,
                        Message = institutionDecision.Message
                            ?? "Institution subscription expired. Please contact your administrator.",
                        CanPurchaseIndividually = true,
                        PurchaseDisabledReason = null
                    };
                }

                if (institutionDecision.IsAllowed)
                {
                    return new DocumentEntitlementDecision
                    {
                        AccessLevel = DocumentAccessLevel.FullAccess,
                        CanPurchaseIndividually = true,
                        PurchaseDisabledReason = null
                    };
                }
            }

            // Default for reports: subscription required
            return new DocumentEntitlementDecision
            {
                AccessLevel = DocumentAccessLevel.PreviewOnly,
                DenyReason = DocumentEntitlementDenyReason.NotEntitled,
                Message = "This report requires an active subscription.",
                CanPurchaseIndividually = true,
                PurchaseDisabledReason = null
            };
        }

        /// <summary>
        /// CORE ENTITLEMENT DECISION ENGINE.
        /// </summary>
        public async Task<DocumentEntitlementDecision> GetEntitlementDecisionAsync(int userId, LegalDocument document)
        {
            var now = DateTime.UtcNow;

            // For analytics (usage events). Not always available (e.g. user not found).
            int? institutionIdForUsage = null;

            // ------------------------------------------------------
            // 1) Free document => always FullAccess
            // ------------------------------------------------------
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

            // ------------------------------------------------------
            // 2) Load user context
            // ------------------------------------------------------
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

            // ------------------------------------------------------
            // 3) Global platform admin => FullAccess
            // ------------------------------------------------------
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

            // ------------------------------------------------------
            // 4) Direct purchase of this specific LegalDocument => FullAccess
            //    ✅ EXCEPT: Reports are subscription-only, so skip this check for reports.
            // ------------------------------------------------------
            if (document.Kind != LegalDocumentKind.Report) // <-- adjust only if your property/enum differs
            {
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
            }

            // ------------------------------------------------------
            // 5) Determine all ContentProduct(s) that include this document
            // ------------------------------------------------------
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

            // ------------------------------------------------------
            // ✅ REPORTS: subscription-only enforcement (+ grace days)
            //    This bypasses ALL non-subscription unlock paths.
            // ------------------------------------------------------
            if (document.Kind == LegalDocumentKind.Report) // <-- adjust only if your property/enum differs
            {
                var rd = await GetReportEntitlementDecisionAsync(
                    userId: userId,
                    institutionId: user.InstitutionId,
                    document: document,
                    productIds: productIds,
                    nowUtc: now,
                    ct: default);

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, rd);
            }

            SubscriptionAccessDecision? institutionDecision = null;

            // Purchase gating defaults (may be overridden for institution accounts)
            bool canPurchaseIndividually = true;
            string? purchaseDisabledReason = null;

            // Track institution state
            bool institutionIsActive = true;

            // ------------------------------------------------------
            // 6) Institution-linked user path:
            // ------------------------------------------------------
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

                if (!(inst?.AllowIndividualPurchasesWhenInstitutionInactive ?? false))
                {
                    canPurchaseIndividually = false;
                    purchaseDisabledReason =
                        "Purchases are disabled for institution accounts. Please contact your administrator.";
                }

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

            // ------------------------------------------------------
            // 7) Library grants => FullAccess (unchanged)
            // ------------------------------------------------------
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

            // ------------------------------------------------------
            // 8) Institution subscription access result
            // ------------------------------------------------------
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

            // ------------------------------------------------------
            // 9) Public ownership of ANY product containing this document => FullAccess
            // ------------------------------------------------------
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

            // ------------------------------------------------------
            // 10) Public subscription (active) to ANY containing product => FullAccess
            // ------------------------------------------------------
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

            // ------------------------------------------------------
            // 11) Default: Not entitled => PreviewOnly
            // ------------------------------------------------------
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
