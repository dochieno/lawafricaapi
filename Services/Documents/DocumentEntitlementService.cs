using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Models.Institutions;
using LawAfrica.API.Services.Subscriptions;
using LawAfrica.API.Services.Usage;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Documents
{
    /// <summary>
    /// SINGLE SOURCE OF TRUTH for document entitlement.
    ///
    /// This service decides whether a user has:
    /// - FullAccess (read all pages)
    /// - PreviewOnly (limited pages in UI)
    ///
    /// It also encodes "hard block" cases using DenyReason, e.g.:
    /// - InstitutionSubscriptionInactive
    /// - InstitutionSeatLimitExceeded
    ///
    /// Controllers and authorization handlers should rely on THIS service
    /// to avoid "works locally but fails in production" mismatches.
    /// </summary>
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
        ///
        /// IMPORTANT: This still calls GetEntitlementDecisionAsync, but it is intended
        /// for high-frequency scenarios (lists, previews, library checks).
        ///
        /// NOTE: Decision logging is currently done inside GetEntitlementDecisionAsync
        /// via LogAndReturnAsync. If you truly want this to be fully "pure",
        /// you'd separate "decision" from "logging". But for now we keep your design.
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
        ///
        /// PURPOSE:
        /// - Record that a user attempted to access/open a document.
        /// - Avoid spamming usage logs due to repeated calls (e.g. scrolling, page loads).
        ///
        /// Throttle window default: 60 seconds (configurable via parameter).
        ///
        /// "surface" default is "ReaderOpen" meaning: a reader load/open.
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
        ///
        /// RULE:
        /// - MaxSeats = 0  => effectively no seats allowed (used > 0 will exceed)
        /// - MaxSeats = N  => allow up to N; exceed when used > N
        ///
        /// Seat usage counts memberships that are:
        /// - Approved
        /// - IsActive == true
        ///
        /// Student seats are counted by MemberType == Student
        /// Staff seats are counted by MemberType == Staff OR InstitutionAdmin
        /// </summary>
        private async Task<(bool exceeded, int usedStudents, int maxStudents, int usedStaff, int maxStaff)>
            IsSeatLimitExceededAsync(int institutionId, CancellationToken ct = default)
        {
            // Read limits from the institution record.
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

            // "Consuming" seats means membership is active + approved.
            var consuming = _db.InstitutionMemberships
                .AsNoTracking()
                .Where(m =>
                    m.InstitutionId == institutionId &&
                    m.IsActive &&
                    m.Status == MembershipStatus.Approved);

            // EF-safe counts
            var usedStudents = await consuming.CountAsync(
                m => m.MemberType == InstitutionMemberType.Student, ct);

            var usedStaff = await consuming.CountAsync(
                m => m.MemberType == InstitutionMemberType.Staff ||
                     m.MemberType == InstitutionMemberType.InstitutionAdmin, ct);

            // Exceeded only if usage strictly greater than max.
            bool exceededStudents = usedStudents > inst.MaxStudentSeats;
            bool exceededStaff = usedStaff > inst.MaxStaffSeats;

            bool exceeded = exceededStudents || exceededStaff;

            return (exceeded, usedStudents, inst.MaxStudentSeats, usedStaff, inst.MaxStaffSeats);
        }

        /// <summary>
        /// CORE ENTITLEMENT DECISION ENGINE.
        ///
        /// Outputs a DocumentEntitlementDecision:
        /// - AccessLevel: FullAccess or PreviewOnly
        /// - DenyReason: None / InstitutionSubscriptionInactive / InstitutionSeatLimitExceeded / NotEntitled
        /// - Message: user-facing string used by UI
        /// - CanPurchaseIndividually + PurchaseDisabledReason: purchase gating rules
        ///
        /// IMPORTANT:
        /// - This method is used by /access and also indirectly by /download.
        /// - Keeping ALL rules here prevents inconsistencies between endpoints.
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
            // 2) Load user context (UserType + InstitutionId)
            //    If user is missing => treat as NotEntitled and allow PreviewOnly
            //    (your current behavior).
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
            // ------------------------------------------------------
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

            // ------------------------------------------------------
            // 5) Determine all ContentProduct(s) that include this document
            //    via join table ContentProductLegalDocuments
            //
            //    Also include document.ContentProductId (legacy/optional direct ref)
            // ------------------------------------------------------
            var productIds = await _db.ContentProductLegalDocuments
                .AsNoTracking()
                .Where(x => x.LegalDocumentId == document.Id)
                .Select(x => x.ContentProductId)
                .Distinct()
                .ToListAsync();

            if (document.ContentProductId.HasValue && !productIds.Contains(document.ContentProductId.Value))
                productIds.Add(document.ContentProductId.Value);

            // If document is not assigned to any product => treat as NotEntitled (preview only).
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

            // Purchase gating defaults (may be overridden for institution accounts)
            bool canPurchaseIndividually = true;
            string? purchaseDisabledReason = null;

            // Track institution state
            bool institutionIsActive = true;

            // ------------------------------------------------------
            // 6) Institution-linked user path:
            //    Apply hard blocks and institution subscription coverage checks.
            // ------------------------------------------------------
            if (user.InstitutionId.HasValue)
            {
                // Read institution flags:
                // - IsActive: institution account active?
                // - AllowIndividualPurchasesWhenInstitutionInactive: can users buy individually when blocked?
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

                // If institution disallows individual purchase in blocked states => disable purchase
                if (!(inst?.AllowIndividualPurchasesWhenInstitutionInactive ?? false))
                {
                    canPurchaseIndividually = false;
                    purchaseDisabledReason =
                        "Purchases are disabled for institution accounts. Please contact your administrator.";
                }

                // 6a) Institution inactive => hard lock
                // (your UI treats this as blocked; /download also denies with headers)
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

                // 6b) Seat limit enforcement at access time
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

                // 6c) Institution subscription coverage:
                // Evaluate whether institution has an active subscription that covers ANY product
                // containing this document.
                institutionDecision = await _subscriptionGuard.EvaluateInstitutionCoverageAsync(
                    institutionId: user.InstitutionId.Value,
                    productIds: productIds,
                    nowUtc: now,
                    graceDays: 0
                );

                // If guard says institution is locked (expired/suspended) => hard lock
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
            // 7) Library grants (manual/administrative grants)
            //    If user has a library record granting access => FullAccess
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
            //    If the institution decision says allowed => FullAccess
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
            //     NOTE: This is where your "preview for public" behavior happens.
            //     Hard-block cases are encoded earlier via DenyReason.
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
