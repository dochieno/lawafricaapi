using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services
{
    /// <summary>
    /// Handles creation/management of institution product subscriptions.
    /// Includes renewals (Rule A) and audit logging.
    /// </summary>
    public class InstitutionSubscriptionService
    {
        private readonly ApplicationDbContext _db;

        public InstitutionSubscriptionService(ApplicationDbContext db)
        {
            _db = db;
        }

        // Backward compatible overload (old callers)
        public Task<InstitutionProductSubscription> CreateOrExtendSubscriptionAsync(
            int institutionId,
            int contentProductId,
            int durationInMonths)
        {
            return CreateOrExtendSubscriptionAsync(institutionId, contentProductId, durationInMonths, null, null);
        }

        /// <summary>
        /// Create or extend a subscription for (InstitutionId, ContentProductId).
        /// If StartDate is in the future => Pending.
        /// Audit: Created or Extended.
        /// </summary>
        public async Task<InstitutionProductSubscription> CreateOrExtendSubscriptionAsync(
            int institutionId,
            int contentProductId,
            int durationInMonths,
            DateTime? startDate,
            int? performedByUserId)
        {
            if (durationInMonths <= 0)
                throw new InvalidOperationException("Duration must be greater than zero.");

            var institution = await _db.Institutions.FindAsync(institutionId);
            if (institution == null)
                throw new InvalidOperationException("Institution not found.");

            var product = await _db.ContentProducts.FindAsync(contentProductId);
            if (product == null)
                throw new InvalidOperationException("Product not found.");

            if (!product.AvailableToInstitutions)
                throw new InvalidOperationException("Product not available to institutions.");

            // ✅ Phase 2/3: institutions use InstitutionAccessModel (fallback to legacy AccessModel if needed)
            var instAccess = product.InstitutionAccessModel != ProductAccessModel.Unknown
                ? product.InstitutionAccessModel
                : product.AccessModel;

            if (instAccess != ProductAccessModel.Subscription)
                throw new InvalidOperationException("Product is not subscription-based for institutions.");

            var now = DateTime.UtcNow;

            var effectiveStart = NormalizeUtc(startDate ?? now);

            var existing = await _db.InstitutionProductSubscriptions
                .FirstOrDefaultAsync(s =>
                    s.InstitutionId == institutionId &&
                    s.ContentProductId == contentProductId);

            if (existing != null)
            {
                var old = Snapshot(existing);

                // Extend from whichever is later: current EndDate or effectiveStart
                var baseDate = existing.EndDate > effectiveStart ? existing.EndDate : effectiveStart;
                existing.EndDate = baseDate.AddMonths(durationInMonths);

                // Do not auto-unsuspend
                if (existing.Status != SubscriptionStatus.Suspended)
                {
                    existing.Status = DeriveStatus(existing.StartDate, existing.EndDate, now);
                }

                await AddAuditAsync(
                    existing,
                    old,
                    SubscriptionAuditAction.Extended,
                    performedByUserId,
                    notes: $"Extended by {durationInMonths} month(s). BaseDate={baseDate:O}"
                );

                await _db.SaveChangesAsync();
                return existing;
            }

            var endDate = effectiveStart.AddMonths(durationInMonths);

            var subscription = new InstitutionProductSubscription
            {
                InstitutionId = institutionId,
                ContentProductId = contentProductId,
                StartDate = effectiveStart,
                EndDate = endDate,
                Status = DeriveStatus(effectiveStart, endDate, now)
            };

            _db.InstitutionProductSubscriptions.Add(subscription);
            await _db.SaveChangesAsync(); // ensure we have subscription.Id

            var oldNew = Snapshot(subscription); // old==new for "Created" entry clarity
            await AddAuditAsync(
                subscription,
                oldNew,
                SubscriptionAuditAction.Created,
                performedByUserId,
                notes: $"Created for {durationInMonths} month(s)."
            );

            await _db.SaveChangesAsync();
            return subscription;
        }

        /// <summary>
        /// Renew a subscription by subscriptionId using Rule A:
        /// - If StartDate provided => use it.
        /// - Else if EndDate > now => renew from EndDate (continuous)
        /// - Else => renew from now
        /// 
        /// Renew sets:
        /// - StartDate stays as-is (original contract start)
        /// - EndDate extended (or restarted if expired)
        /// - Status recalculated unless suspended (don’t auto-unsuspend)
        /// Audit: Renewed
        /// </summary>
        public async Task<InstitutionProductSubscription> RenewSubscriptionAsync(
            int subscriptionId,
            int durationInMonths,
            DateTime? startDate,
            int? performedByUserId)
        {
            if (durationInMonths <= 0)
                throw new InvalidOperationException("Duration must be greater than zero.");

            var sub = await _db.InstitutionProductSubscriptions
                .FirstOrDefaultAsync(x => x.Id == subscriptionId);

            if (sub == null)
                throw new InvalidOperationException("Subscription not found.");

            // ✅ Defense-in-depth (even if controller already blocks it)
            if (sub.Status == SubscriptionStatus.Suspended)
                throw new InvalidOperationException("Cannot renew a suspended subscription. Unsuspend it first.");

            var now = DateTime.UtcNow;
            var old = Snapshot(sub);

            // Normalize startDate if provided
            DateTime? normalizedStart = null;
            if (startDate.HasValue)
                normalizedStart = NormalizeUtc(startDate.Value);

            // ✅ Rule A:
            // - if startDate provided, renewal starts there
            // - else if still active, renew from current EndDate
            // - else renew from now
            var effectiveStart = normalizedStart.HasValue
                ? normalizedStart.Value
                : (sub.EndDate > now ? sub.EndDate : now);

            // ✅ Extend end date from effective start
            sub.EndDate = effectiveStart.AddMonths(durationInMonths);

            // ✅ Status should always reflect start/end relationship (unless suspended—already blocked)
            sub.Status = DeriveStatus(sub.StartDate, sub.EndDate, now);

            await AddAuditAsync(
                sub,
                old,
                SubscriptionAuditAction.Renewed,
                performedByUserId,
                notes: $"Renewed by {durationInMonths} month(s). EffectiveStart={effectiveStart:O}"
            );

            await _db.SaveChangesAsync();
            return sub;
        }

        public async Task<InstitutionProductSubscription> SuspendAsync(int subscriptionId, int? performedByUserId)
        {
            var sub = await _db.InstitutionProductSubscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId);
            if (sub == null) throw new InvalidOperationException("Subscription not found.");

            var old = Snapshot(sub);

            sub.Status = SubscriptionStatus.Suspended;

            await AddAuditAsync(
                sub,
                old,
                SubscriptionAuditAction.Suspended,
                performedByUserId,
                notes: "Suspended (EndDate not changed)."
            );

            await _db.SaveChangesAsync();
            return sub;
        }

        public async Task<InstitutionProductSubscription> UnsuspendAsync(int subscriptionId, int? performedByUserId)
        {
            var sub = await _db.InstitutionProductSubscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId);
            if (sub == null) throw new InvalidOperationException("Subscription not found.");

            var now = DateTime.UtcNow;
            var old = Snapshot(sub);

            if (sub.StartDate > now)
                sub.Status = SubscriptionStatus.Pending;
            else if (sub.EndDate <= now)
                sub.Status = SubscriptionStatus.Expired;
            else
                sub.Status = SubscriptionStatus.Active;

            await AddAuditAsync(
                sub,
                old,
                SubscriptionAuditAction.Unsuspended,
                performedByUserId,
                notes: "Unsuspended (status derived from dates)."
            );

            await _db.SaveChangesAsync();
            return sub;
        }

        private static SubscriptionStatus DeriveStatus(DateTime startDate, DateTime endDate, DateTime now)
        {
            if (startDate > now) return SubscriptionStatus.Pending;
            if (endDate <= now) return SubscriptionStatus.Expired;
            return SubscriptionStatus.Active;
        }

        private static DateTime NormalizeUtc(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Unspecified)
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            if (dt.Kind == DateTimeKind.Local)
                return dt.ToUniversalTime();
            return dt;
        }

        private static (DateTime Start, DateTime End, SubscriptionStatus Status) Snapshot(InstitutionProductSubscription s)
            => (s.StartDate, s.EndDate, s.Status);

        private async Task AddAuditAsync(
            InstitutionProductSubscription sub,
            (DateTime Start, DateTime End, SubscriptionStatus Status) old,
            SubscriptionAuditAction action,
            int? performedByUserId,
            string? notes)
        {
            var audit = new InstitutionSubscriptionAudit
            {
                SubscriptionId = sub.Id,
                Action = action,
                PerformedByUserId = performedByUserId,

                OldStartDate = old.Start,
                OldEndDate = old.End,
                OldStatus = old.Status,

                NewStartDate = sub.StartDate,
                NewEndDate = sub.EndDate,
                NewStatus = sub.Status,

                Notes = notes,
                CreatedAt = DateTime.UtcNow
            };

            _db.InstitutionSubscriptionAudits.Add(audit);
            await Task.CompletedTask;
        }

        //Additional Methods:

        public async Task<InstitutionSubscriptionActionRequest> RequestSuspendAsync(
        int subscriptionId,
        int requestedByUserId,
        string? notes)
            {
                var sub = await _db.InstitutionProductSubscriptions
                    .FirstOrDefaultAsync(x => x.Id == subscriptionId);

                if (sub == null) throw new InvalidOperationException("Subscription not found.");

                // Don’t allow duplicate pending requests
                var hasPending = await _db.InstitutionSubscriptionActionRequests.AnyAsync(r =>
                    r.SubscriptionId == subscriptionId &&
                    r.RequestType == SubscriptionActionRequestType.Suspend &&
                    r.Status == SubscriptionActionRequestStatus.Pending);

                if (hasPending)
                    throw new InvalidOperationException("There is already a pending suspend request for this subscription.");

                var req = new InstitutionSubscriptionActionRequest
                {
                    SubscriptionId = subscriptionId,
                    RequestType = SubscriptionActionRequestType.Suspend,
                    Status = SubscriptionActionRequestStatus.Pending,
                    RequestedByUserId = requestedByUserId,
                    RequestNotes = notes
                };

                _db.InstitutionSubscriptionActionRequests.Add(req);
                await _db.SaveChangesAsync();
                return req;
            }

            public async Task<InstitutionSubscriptionActionRequest> RequestUnsuspendAsync(
                int subscriptionId,
                int requestedByUserId,
                string? notes)
            {
                var sub = await _db.InstitutionProductSubscriptions
                    .FirstOrDefaultAsync(x => x.Id == subscriptionId);

                if (sub == null) throw new InvalidOperationException("Subscription not found.");

                var hasPending = await _db.InstitutionSubscriptionActionRequests.AnyAsync(r =>
                    r.SubscriptionId == subscriptionId &&
                    r.RequestType == SubscriptionActionRequestType.Unsuspend &&
                    r.Status == SubscriptionActionRequestStatus.Pending);

                if (hasPending)
                    throw new InvalidOperationException("There is already a pending unsuspend request for this subscription.");

                var req = new InstitutionSubscriptionActionRequest
                {
                    SubscriptionId = subscriptionId,
                    RequestType = SubscriptionActionRequestType.Unsuspend,
                    Status = SubscriptionActionRequestStatus.Pending,
                    RequestedByUserId = requestedByUserId,
                    RequestNotes = notes
                };

                _db.InstitutionSubscriptionActionRequests.Add(req);
                await _db.SaveChangesAsync();
                return req;
            }

            public async Task<InstitutionSubscriptionActionRequest> ApproveRequestAsync(
                int requestId,
                int reviewedByUserId,
                bool approve,
                string? reviewNotes)
            {
                var req = await _db.InstitutionSubscriptionActionRequests
                    .Include(r => r.Subscription)
                    .FirstOrDefaultAsync(r => r.Id == requestId);

                if (req == null) throw new InvalidOperationException("Request not found.");

                if (req.Status != SubscriptionActionRequestStatus.Pending)
                    throw new InvalidOperationException("Only pending requests can be reviewed.");

                req.Status = approve ? SubscriptionActionRequestStatus.Approved : SubscriptionActionRequestStatus.Rejected;
                req.ReviewedByUserId = reviewedByUserId;
                req.ReviewedAt = DateTime.UtcNow;
                req.ReviewNotes = reviewNotes;

                if (approve)
                {
                    // Apply action
                    if (req.RequestType == SubscriptionActionRequestType.Suspend)
                    {
                        await SuspendAsync(req.SubscriptionId, reviewedByUserId);
                    }
                    else if (req.RequestType == SubscriptionActionRequestType.Unsuspend)
                    {
                        await UnsuspendAsync(req.SubscriptionId, reviewedByUserId);
                    }
                }
                else
                {
                    await _db.SaveChangesAsync();
                }

                return req;
            }

    }
}
