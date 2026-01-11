using System;
using System.Linq;
using System.Threading.Tasks;
using LawAfrica.API.Data;
using LawAfrica.API.Models.DTOs.AdminDashboard;
using LawAfrica.API.Models.Institutions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/institutions")]
    [Authorize(Roles = "Admin")]
    public class AdminInstitutionsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AdminInstitutionsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<AdminInstitutionRowDto>>> List([FromQuery] AdminInstitutionsQuery query)
        {
            var now = DateTime.UtcNow;

            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize is < 1 ? 20 : (query.PageSize > 200 ? 200 : query.PageSize);

            var q = (query.Q ?? "").Trim();

            var institutionsQ = _db.Institutions.AsNoTracking();

            // basic filters
            if (query.IsActive.HasValue)
            {
                // NOTE: assumes Institution.IsActive exists. If not, tell me and I’ll convert this filter.
                institutionsQ = institutionsQ.Where(i => i.IsActive == query.IsActive.Value);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                institutionsQ = institutionsQ.Where(i =>
                    i.Name.Contains(q) ||
                    i.EmailDomain.Contains(q) ||
                    i.OfficialEmail.Contains(q)
                );
            }

            // Project into rows with computed fields using correlated subqueries.
            // This is efficient enough for admin paging (and avoids loading graphs).
            var projected = institutionsQ.Select(i => new
            {
                i.Id,
                i.Name,
                i.EmailDomain,
                i.OfficialEmail,
                i.IsActive,
                CreatedAt = i.CreatedAt,

                i.MaxStudentSeats,
                i.MaxStaffSeats,

                HasActiveSub = _db.InstitutionProductSubscriptions.Any(s =>
                    s.InstitutionId == i.Id &&
                    s.StartDate <= now &&
                    s.EndDate > now
                ),

                NextEnd = _db.InstitutionProductSubscriptions
                    .Where(s => s.InstitutionId == i.Id && s.EndDate > now)
                    .OrderBy(s => s.EndDate)
                    .Select(s => (DateTime?)s.EndDate)
                    .FirstOrDefault(),

                StudentUsed = _db.InstitutionMemberships.Count(m =>
                    m.InstitutionId == i.Id &&
                    m.IsActive &&
                    m.Status == MembershipStatus.Approved &&
                    m.MemberType == InstitutionMemberType.Student
                ),

                StaffUsed = _db.InstitutionMemberships.Count(m =>
                    m.InstitutionId == i.Id &&
                    m.IsActive &&
                    m.Status == MembershipStatus.Approved &&
                    m.MemberType == InstitutionMemberType.Staff
                ),

                StudentPending = _db.InstitutionMemberships.Count(m =>
                    m.InstitutionId == i.Id &&
                    m.IsActive &&
                    m.Status == MembershipStatus.PendingApproval &&
                    m.MemberType == InstitutionMemberType.Student
                ),

                StaffPending = _db.InstitutionMemberships.Count(m =>
                    m.InstitutionId == i.Id &&
                    m.IsActive &&
                    m.Status == MembershipStatus.PendingApproval &&
                    m.MemberType == InstitutionMemberType.Staff
                )
            });

            // computed filters (apply after projection so we can use computed fields)
            if (query.SeatBlocked.HasValue)
            {
                projected = projected.Where(x =>
                    (x.MaxStudentSeats == 0 || x.MaxStaffSeats == 0) == query.SeatBlocked.Value
                );
            }

            if (query.HasActiveSubscription.HasValue)
            {
                projected = projected.Where(x => x.HasActiveSub == query.HasActiveSubscription.Value);
            }

            if (query.IsLocked.HasValue)
            {
                projected = projected.Where(x => (!x.HasActiveSub) == query.IsLocked.Value);
            }

            if (query.AtCapacity.HasValue)
            {
                projected = projected.Where(x =>
                    (
                        (x.MaxStudentSeats > 0 && x.StudentUsed >= x.MaxStudentSeats) ||
                        (x.MaxStaffSeats > 0 && x.StaffUsed >= x.MaxStaffSeats)
                    ) == query.AtCapacity.Value
                );
            }

            // sorting
            projected = (query.Sort ?? "").ToLowerInvariant() switch
            {
                "name_asc" => projected.OrderBy(x => x.Name),
                "name_desc" => projected.OrderByDescending(x => x.Name),

                "createdat_asc" => projected.OrderBy(x => x.CreatedAt),
                "createdat_desc" => projected.OrderByDescending(x => x.CreatedAt),

                "nextend_asc" => projected.OrderBy(x => x.NextEnd),
                "nextend_desc" => projected.OrderByDescending(x => x.NextEnd),

                _ => projected.OrderByDescending(x => x.CreatedAt)
            };

            var total = await projected.CountAsync();

            var items = await projected
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new AdminInstitutionRowDto(
                    InstitutionId: x.Id,
                    Name: x.Name,
                    EmailDomain: x.EmailDomain,
                    OfficialEmail: x.OfficialEmail,
                    IsActive: x.IsActive,
                    CreatedAtUtc: DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),

                    HasActiveSubscription: x.HasActiveSub,
                    IsLocked: !x.HasActiveSub,
                    NextSubscriptionEndUtc: x.NextEnd.HasValue ? DateTime.SpecifyKind(x.NextEnd.Value, DateTimeKind.Utc) : null,

                    MaxStudentSeats: x.MaxStudentSeats,
                    MaxStaffSeats: x.MaxStaffSeats,
                    StudentUsed: x.StudentUsed,
                    StaffUsed: x.StaffUsed,
                    StudentPending: x.StudentPending,
                    StaffPending: x.StaffPending
                ))
                .ToListAsync();

            return Ok(new PagedResult<AdminInstitutionRowDto>(items, page, pageSize, total));
        }
    }
}
