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

        public async Task<DocumentAccessLevel> GetAccessLevelAsync(int userId, LegalDocument document)
        {
            var decision = await GetEntitlementDecisionAsync(userId, document);
            return decision.AccessLevel;
        }

        // ---------------------------
        // Helpers
        // ---------------------------

        private static bool CountsAsStudent(InstitutionMemberType t) => t == InstitutionMemberType.Student;

        private static bool CountsAsStaff(InstitutionMemberType t)
            => t == InstitutionMemberType.Staff || t == InstitutionMemberType.InstitutionAdmin;

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
            var reason = allowed ? "ALLOWED" : decision.DenyReason.ToString();

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

            var usedStudents = await consuming.CountAsync(m => m.MemberType == InstitutionMemberType.Student, ct);

            var usedStaff = await consuming.CountAsync(
                m => m.MemberType == InstitutionMemberType.Staff ||
                     m.MemberType == InstitutionMemberType.InstitutionAdmin, ct);

            bool exceededStudents = usedStudents > inst.MaxStudentSeats;
            bool exceededStaff = usedStaff > inst.MaxStaffSeats;

            return (exceededStudents || exceededStaff, usedStudents, inst.MaxStudentSeats, usedStaff, inst.MaxStaffSeats);
        }

        // ---------------------------
        // Reports settings (env for now)
        // ---------------------------

        private Task<int> GetReportGraceDaysAsync(CancellationToken ct = default)
        {
            var raw = Environment.GetEnvironmentVariable("REPORT_GRACE_DAYS");
            if (int.TryParse(raw, out var days))
            {
                if (days < 0) days = 0;
                if (days > 365) days = 365;
                return Task.FromResult(days);
            }
            return Task.FromResult(0);
        }

        private Task<int> GetReportPreviewMaxCharsAsync(CancellationToken ct = default)
        {
            var raw = Environment.GetEnvironmentVariable("REPORT_PREVIEW_MAX_CHARS");
            if (int.TryParse(raw, out var v))
            {
                if (v < 200) v = 200;
                if (v > 50_000) v = 50_000;
                return Task.FromResult(v);
            }
            // sensible default
            return Task.FromResult(2200);
        }

        private Task<int> GetReportPreviewMaxParagraphsAsync(CancellationToken ct = default)
        {
            var raw = Environment.GetEnvironmentVariable("REPORT_PREVIEW_MAX_PARAGRAPHS");
            if (int.TryParse(raw, out var v))
            {
                if (v < 1) v = 1;
                if (v > 200) v = 200;
                return Task.FromResult(v);
            }
            // sensible default
            return Task.FromResult(6);
        }

        // ---------------------------
        // Resolve product required for gating
        // ---------------------------

        private async Task<(int? id, string? name)> ResolvePrimaryRequiredProductAsync(
            IReadOnlyCollection<int> productIds,
            CancellationToken ct = default)
        {
            if (productIds == null || productIds.Count == 0)
                return (null, null);

            // If multiple productIds exist, pick the first deterministically by id
            var pick = productIds.Min();

            var prod = await _db.ContentProducts
                .AsNoTracking()
                .Where(p => p.Id == pick)
                .Select(p => new { p.Id, p.Name })
                .FirstOrDefaultAsync(ct);

            return prod == null ? (pick, null) : (prod.Id, prod.Name);
        }

        // =========================================================
        // REPORTS: subscription-only entitlement
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

            var (requiredProductId, requiredProductName) =
                await ResolvePrimaryRequiredProductAsync(productIds, ct);

            var previewChars = await GetReportPreviewMaxCharsAsync(ct);
            var previewParas = await GetReportPreviewMaxParagraphsAsync(ct);

            // (A) Personal subscription first (independent)
            var hasUserSubscription = await _db.UserProductSubscriptions
                .AsNoTracking()
                .AnyAsync(s =>
                    s.UserId == userId &&
                    productIds.Contains(s.ContentProductId) &&
                    s.Status == SubscriptionStatus.Active &&
                    s.StartDate <= nowUtc &&
                    s.EndDate >= nowUtc, ct);

            //commented here:
           // s.endDate>=graceCutoff,ct);

            if (hasUserSubscription)
            {
                return new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    CanPurchaseIndividually = true,

                    GrantSource = EntitlementGrantSource.PersonalSubscription,
                    DebugNote = "Report allowed by personal subscription."
                };
            }

            // (B) Institution subscription path (only if linked to institution)
            if (institutionId.HasValue)
            {
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

                        // gate UI
                        RequiredProductId = requiredProductId,
                        RequiredProductName = requiredProductName,
                        RequiredAction = EntitlementRequiredAction.Subscribe,
                        CtaLabel = "Subscribe to continue",
                        CtaUrl = requiredProductId.HasValue ? $"/pricing?productId={requiredProductId.Value}" : "/pricing",
                        PreviewMaxChars = previewChars,
                        PreviewMaxParagraphs = previewParas,
                        HardStop = true,

                        GrantSource = EntitlementGrantSource.None,
                        DebugNote = "Report blocked: institution inactive, no personal sub."
                    };
                }

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

                        // gate UI
                        RequiredProductId = requiredProductId,
                        RequiredProductName = requiredProductName,
                        RequiredAction = EntitlementRequiredAction.Subscribe,
                        CtaLabel = "Subscribe to continue",
                        CtaUrl = requiredProductId.HasValue ? $"/pricing?productId={requiredProductId.Value}" : "/pricing",
                        PreviewMaxChars = previewChars,
                        PreviewMaxParagraphs = previewParas,
                        HardStop = true,

                        GrantSource = EntitlementGrantSource.None,
                        DebugNote = "Report blocked: institution seat limit exceeded, no personal sub."
                    };
                }

                var institutionDecision = await _subscriptionGuard.EvaluateInstitutionCoverageAsync(
                    institutionId: institutionId.Value,
                    productIds: productIds,
                    nowUtc: nowUtc,
                    graceDays: graceDays,
                    ct: ct
                );

                if (institutionDecision.IsAllowed)
                {
                    return new DocumentEntitlementDecision
                    {
                        AccessLevel = DocumentAccessLevel.FullAccess,
                        CanPurchaseIndividually = true,

                        GrantSource = EntitlementGrantSource.InstitutionSubscription,
                        DebugNote = "Report allowed by institution subscription."
                    };
                }

                if (institutionDecision.IsInstitutionLock)
                {
                    return new DocumentEntitlementDecision
                    {
                        AccessLevel = DocumentAccessLevel.PreviewOnly,
                        DenyReason = DocumentEntitlementDenyReason.InstitutionSubscriptionInactive,
                        Message = institutionDecision.Message ?? "Institution subscription inactive. Please contact your administrator.",
                        CanPurchaseIndividually = true,

                        RequiredProductId = requiredProductId,
                        RequiredProductName = requiredProductName,
                        RequiredAction = EntitlementRequiredAction.Subscribe,
                        CtaLabel = "Subscribe to continue",
                        CtaUrl = requiredProductId.HasValue ? $"/pricing?productId={requiredProductId.Value}" : "/pricing",
                        PreviewMaxChars = previewChars,
                        PreviewMaxParagraphs = previewParas,
                        HardStop = true,

                        GrantSource = EntitlementGrantSource.None,
                        DebugNote = $"Report institution lock: {institutionDecision.Reason}"
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

                RequiredProductId = requiredProductId,
                RequiredProductName = requiredProductName,
                RequiredAction = EntitlementRequiredAction.Subscribe,
                CtaLabel = "Subscribe to continue",
                CtaUrl = requiredProductId.HasValue ? $"/pricing?productId={requiredProductId.Value}" : "/pricing",
                SecondaryCtaLabel = "View plans",
                SecondaryCtaUrl = "/pricing",
                PreviewMaxChars = previewChars,
                PreviewMaxParagraphs = previewParas,
                HardStop = true,

                GrantSource = EntitlementGrantSource.None,
                DebugNote = "Report denied: no personal sub and no institution coverage."
            };
        }

        // =========================================================
        // CORE ENTITLEMENT DECISION ENGINE
        // =========================================================
        public async Task<DocumentEntitlementDecision> GetEntitlementDecisionAsync(int userId, LegalDocument document)
        {
            var now = DateTime.UtcNow;
            int? institutionIdForUsage = null;

            // 1) Free document
            if (!document.IsPremium)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    GrantSource = EntitlementGrantSource.None,
                    DebugNote = "Non-premium document."
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 2) Load user context
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
                    RequiredAction = EntitlementRequiredAction.None,
                    GrantSource = EntitlementGrantSource.None,
                    DebugNote = "User not found."
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            institutionIdForUsage = user.InstitutionId;

            // 3) Global admin
            if (user.UserType == UserType.Admin)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    GrantSource = EntitlementGrantSource.GlobalAdmin,
                    DebugNote = "UserType Admin."
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 4) Skip direct purchase for reports (subscription-only)
            if (document.Kind != LegalDocumentKind.Report)
            {
                var boughtThisDocument = await _db.UserLegalDocumentPurchases
                    .AsNoTracking()
                    .AnyAsync(p => p.UserId == userId && p.LegalDocumentId == document.Id);

                if (boughtThisDocument)
                {
                    var d = new DocumentEntitlementDecision
                    {
                        AccessLevel = DocumentAccessLevel.FullAccess,
                        GrantSource = EntitlementGrantSource.DirectPurchase,
                        DebugNote = "Allowed by direct document purchase."
                    };

                    return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
                }
            }

            // 5) Determine productIds containing this document
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
                    DebugNote = "No product mapping found for document."
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 6) Reports: subscription-only gate
            if (document.Kind == LegalDocumentKind.Report)
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

            bool canPurchaseIndividually = true;
            string? purchaseDisabledReason = null;

            // 7) Institution-linked user path
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

                var institutionIsActive = inst?.IsActive ?? true;

                if (!(inst?.AllowIndividualPurchasesWhenInstitutionInactive ?? false))
                {
                    canPurchaseIndividually = false;
                    purchaseDisabledReason = "Purchases are disabled for institution accounts. Please contact your administrator.";
                }

                if (!institutionIsActive)
                {
                    var d = new DocumentEntitlementDecision
                    {
                        AccessLevel = DocumentAccessLevel.PreviewOnly,
                        DenyReason = DocumentEntitlementDenyReason.InstitutionSubscriptionInactive,
                        Message = "Your institution is inactive. Please contact your administrator.",
                        CanPurchaseIndividually = canPurchaseIndividually,
                        PurchaseDisabledReason = purchaseDisabledReason,
                        DebugNote = "Institution inactive."
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
                        PurchaseDisabledReason = purchaseDisabledReason,
                        DebugNote = "Seat limit exceeded."
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
                        Message = institutionDecision.Message ?? "Institution subscription expired. Please contact your administrator.",
                        CanPurchaseIndividually = canPurchaseIndividually,
                        PurchaseDisabledReason = purchaseDisabledReason,
                        DebugNote = $"Institution lock: {institutionDecision.Reason}"
                    };

                    return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
                }
            }

            // 8) Library grants => FullAccess
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
                    GrantSource = EntitlementGrantSource.LibraryGrant,
                    DebugNote = "Allowed by library grant."
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 9) Institution subscription allowed
            if (institutionDecision != null && institutionDecision.IsAllowed)
            {
                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.FullAccess,
                    GrantSource = EntitlementGrantSource.InstitutionSubscription,
                    DebugNote = "Allowed by institution subscription."
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 10) Public ownership of ANY containing product
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
                    GrantSource = EntitlementGrantSource.ProductOwnership,
                    DebugNote = "Allowed by product ownership."
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 11) Public subscription to ANY containing product
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
                    GrantSource = EntitlementGrantSource.PersonalSubscription,
                    DebugNote = "Allowed by personal subscription."
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }

            // 12) Default: not entitled => PreviewOnly + gate suggestion (Buy if purchasable)
            {
                var (requiredProductId, requiredProductName) =
                    await ResolvePrimaryRequiredProductAsync(productIds, default);

                var purchasable = document.AllowPublicPurchase && (document.PublicPrice ?? 0) > 0;

                var d = new DocumentEntitlementDecision
                {
                    AccessLevel = DocumentAccessLevel.PreviewOnly,
                    DenyReason = DocumentEntitlementDenyReason.NotEntitled,
                    Message = "You do not have access to this document.",
                    CanPurchaseIndividually = canPurchaseIndividually,
                    PurchaseDisabledReason = purchaseDisabledReason,

                    RequiredProductId = requiredProductId,
                    RequiredProductName = requiredProductName,
                    RequiredAction = purchasable ? EntitlementRequiredAction.Buy : EntitlementRequiredAction.None,
                    CtaLabel = purchasable ? "Buy to continue" : null,
                    CtaUrl = purchasable ? $"/checkout?docId={document.Id}" : null,
                    SecondaryCtaLabel = purchasable ? "View offer" : null,
                    SecondaryCtaUrl = purchasable ? $"/documents/{document.Id}" : null,

                    HardStop = false,
                    GrantSource = EntitlementGrantSource.None,
                    DebugNote = purchasable ? "Not entitled; doc is purchasable." : "Not entitled; no purchase offer."
                };

                return await LogAndReturnAsync(userId, institutionIdForUsage, document.Id, d);
            }
        }
    }
}
