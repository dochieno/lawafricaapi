using System;
using System.Linq;
using System.Threading.Tasks;
using LawAfrica.API.Data;
using LawAfrica.API.Models.DTOs.AdminDashboard;
using LawAfrica.API.Models.Institutions;
using LawAfrica.API.Models.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/dashboard")]
    [Authorize(Roles = "Admin")]
    public class AdminDashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AdminDashboardController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet("overview")]
        public async Task<ActionResult<AdminDashboardOverviewResponse>> GetOverview([FromQuery] AdminDashboardOverviewQuery query)
        {
            var now = DateTime.UtcNow;

            var toUtc = query.ToUtc?.ToUniversalTime() ?? now;
            var fromUtc = query.FromUtc?.ToUniversalTime() ?? toUtc.AddDays(-30);

            if (fromUtc >= toUtc)
                return BadRequest("FromUtc must be earlier than ToUtc.");

            var expDays = query.ExpiringSoonDays <= 0 ? 14 : query.ExpiringSoonDays;
            var expiringTo = now.AddDays(expDays);

            // ----------------------------
            // Institutions KPIs (SEQUENTIAL awaits)
            // ----------------------------
            var institutionsTotal = await _db.Institutions.AsNoTracking().CountAsync();

            var institutionsActive = await _db.Institutions.AsNoTracking().CountAsync(i => i.IsActive);

            var institutionsNewInPeriod = await _db.Institutions.AsNoTracking().CountAsync(i =>
                i.CreatedAt >= fromUtc && i.CreatedAt < toUtc
            );

            var institutionsLockedBySub = await
                (from inst in _db.Institutions.AsNoTracking()
                 let hasActiveSub = _db.InstitutionProductSubscriptions.AsNoTracking().Any(s =>
                     s.InstitutionId == inst.Id &&
                     s.StartDate <= now &&
                     s.EndDate > now
                 )
                 select hasActiveSub
                ).CountAsync(hasActiveSub => !hasActiveSub);

            // ----------------------------
            // Subscription KPIs (SEQUENTIAL awaits)
            // ----------------------------
            var subsTotal = await _db.InstitutionProductSubscriptions.AsNoTracking().CountAsync();

            var subsActiveNow = await _db.InstitutionProductSubscriptions.AsNoTracking().CountAsync(s =>
                s.StartDate <= now && s.EndDate > now
            );

            var subsExpiringSoon = await _db.InstitutionProductSubscriptions.AsNoTracking().CountAsync(s =>
                s.StartDate <= now &&
                s.EndDate > now &&
                s.EndDate <= expiringTo
            );

            // ----------------------------
            // Seats KPIs (InstitutionMembership) (SEQUENTIAL awaits)
            // ----------------------------
            var studentUsed = await _db.InstitutionMemberships.AsNoTracking().CountAsync(m =>
                m.IsActive &&
                m.Status == MembershipStatus.Approved &&
                m.MemberType == InstitutionMemberType.Student
            );

            // ✅ Staff bucket includes Staff + InstitutionAdmin (per your rule)
            var staffUsed = await _db.InstitutionMemberships.AsNoTracking().CountAsync(m =>
                m.IsActive &&
                m.Status == MembershipStatus.Approved &&
                (m.MemberType == InstitutionMemberType.Staff || m.MemberType == InstitutionMemberType.InstitutionAdmin)
            );

            var adminsUsed = await _db.InstitutionMemberships.AsNoTracking().CountAsync(m =>
                m.IsActive &&
                m.Status == MembershipStatus.Approved &&
                m.MemberType == InstitutionMemberType.InstitutionAdmin
            );

            var studentPending = await _db.InstitutionMemberships.AsNoTracking().CountAsync(m =>
                m.IsActive &&
                m.Status == MembershipStatus.PendingApproval &&
                m.MemberType == InstitutionMemberType.Student
            );

            var staffPending = await _db.InstitutionMemberships.AsNoTracking().CountAsync(m =>
                m.IsActive &&
                m.Status == MembershipStatus.PendingApproval &&
                m.MemberType == InstitutionMemberType.Staff
            );

            var institutionsSeatBlocked = await _db.Institutions.AsNoTracking().CountAsync(i =>
                i.MaxStudentSeats == 0 || i.MaxStaffSeats == 0
            );

            var institutionsAtCapacity = await
                (from i in _db.Institutions.AsNoTracking()
                 let studentUsedSeats = _db.InstitutionMemberships.AsNoTracking().Count(m =>
                     m.InstitutionId == i.Id &&
                     m.IsActive &&
                     m.Status == MembershipStatus.Approved &&
                     m.MemberType == InstitutionMemberType.Student
                 )
                 let staffUsedSeats = _db.InstitutionMemberships.AsNoTracking().Count(m =>
                     m.InstitutionId == i.Id &&
                     m.IsActive &&
                     m.Status == MembershipStatus.Approved &&
                     (m.MemberType == InstitutionMemberType.Staff || m.MemberType == InstitutionMemberType.InstitutionAdmin)
                 )
                 select new
                 {
                     i.MaxStudentSeats,
                     i.MaxStaffSeats,
                     StudentUsed = studentUsedSeats,
                     StaffUsed = staffUsedSeats
                 })
                .CountAsync(x =>
                    (x.MaxStudentSeats > 0 && x.StudentUsed >= x.MaxStudentSeats) ||
                    (x.MaxStaffSeats > 0 && x.StaffUsed >= x.MaxStaffSeats)
                );

            var seatMaxTotals = await _db.Institutions.AsNoTracking()
                .Select(i => new { i.MaxStudentSeats, i.MaxStaffSeats })
                .ToListAsync();

            var maxStudentTotal = seatMaxTotals.Sum(x => x.MaxStudentSeats);
            var maxStaffTotal = seatMaxTotals.Sum(x => x.MaxStaffSeats);

            // ----------------------------
            // Payments KPIs (SEQUENTIAL awaits)
            // ----------------------------
            var paymentsInPeriodQuery = _db.PaymentIntents.AsNoTracking()
                .Where(p => p.CreatedAt >= fromUtc && p.CreatedAt < toUtc);

            var paymentsTotalCount = await paymentsInPeriodQuery.CountAsync();
            var paymentsTotalAmount = (await paymentsInPeriodQuery.SumAsync(p => (decimal?)p.Amount)) ?? 0m;

            var paymentsSuccessAmount = (await paymentsInPeriodQuery
                .Where(p => p.Status == PaymentStatus.Success)
                .SumAsync(p => (decimal?)p.Amount)) ?? 0m;

            var paymentsFailedCount = await paymentsInPeriodQuery.CountAsync(p => p.Status == PaymentStatus.Failed);

            // ✅ SUCCESS-only split
            var paymentsInstitutionAmount = (await paymentsInPeriodQuery
                .Where(p => p.Status == PaymentStatus.Success && p.InstitutionId != null)
                .SumAsync(p => (decimal?)p.Amount)) ?? 0m;

            var paymentsIndividualAmount = (await paymentsInPeriodQuery
                .Where(p => p.Status == PaymentStatus.Success && p.InstitutionId == null)
                .SumAsync(p => (decimal?)p.Amount)) ?? 0m;

            var amountByPurpose = await paymentsInPeriodQuery
                .Where(p => p.Status == PaymentStatus.Success)
                .GroupBy(p => p.Purpose)
                .Select(g => new KeyValuePoint(g.Key.ToString(), g.Sum(x => x.Amount)))
                .ToListAsync();

            var countByStatus = await paymentsInPeriodQuery
                .GroupBy(p => p.Status)
                .Select(g => new KeyValuePoint(g.Key.ToString(), g.Count()))
                .ToListAsync();

            // ----------------------------
            // Usage KPIs (placeholder here)
            // Actual charts come from /api/admin/usage/summary
            // ----------------------------
            var usageReads = 0;
            var usageBlocks = 0;

            var totalAttempts = usageReads + usageBlocks;
            var blockRate = totalAttempts == 0 ? 0m : Math.Round((decimal)usageBlocks / totalAttempts, 4);

            var response = new AdminDashboardOverviewResponse(
                FromUtc: fromUtc,
                ToUtc: toUtc,

                Institutions: new AdminInstitutionsKpis(
                    Total: institutionsTotal,
                    Active: institutionsActive,
                    LockedBySubscription: institutionsLockedBySub,
                    NewInPeriod: institutionsNewInPeriod
                ),

                Subscriptions: new AdminSubscriptionsKpis(
                    Total: subsTotal,
                    ActiveNow: subsActiveNow,
                    InactiveNow: Math.Max(0, subsTotal - subsActiveNow),
                    ExpiringSoon: subsExpiringSoon,
                    ExpiringSoonDays: expDays
                ),

                Seats: new AdminSeatsKpis(
                    InstitutionsAtCapacity: institutionsAtCapacity,
                    InstitutionsSeatBlocked: institutionsSeatBlocked,
                    StudentUsed: studentUsed,
                    StudentPending: studentPending,
                    StaffUsed: staffUsed,
                    StaffPending: staffPending,
                    AdminsUsed: adminsUsed,
                    MaxStudentSeatsTotal: maxStudentTotal,
                    MaxStaffSeatsTotal: maxStaffTotal
                ),

                Payments: new AdminPaymentsKpis(
                    TotalCount: paymentsTotalCount,
                    TotalAmount: paymentsTotalAmount,
                    SuccessAmount: paymentsSuccessAmount,
                    FailedCount: paymentsFailedCount,
                    InstitutionAmount: paymentsInstitutionAmount,
                    IndividualAmount: paymentsIndividualAmount
                ),

                Usage: new AdminUsageKpis(
                    Reads: usageReads,
                    Blocks: usageBlocks,
                    BlockRate: blockRate,
                    TopDocuments: Array.Empty<KeyValuePoint>(),
                    TopInstitutions: Array.Empty<KeyValuePoint>()
                ),

                DenyReasons: new AdminDenyReasonBreakdown(
                    ByReason: Array.Empty<KeyValuePoint>()
                ),

                PaymentsBreakdown: new AdminPaymentsBreakdown(
                    AmountByPurpose: amountByPurpose,
                    CountByStatus: countByStatus
                )
            );

            return Ok(response);
        }
    }
}
