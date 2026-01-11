using System;
using System.Linq;
using System.Threading.Tasks;
using LawAfrica.API.Data;
using LawAfrica.API.Models.DTOs.AdminDashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/subscriptions")]
    [Authorize(Roles = "Admin")]
    public class AdminSubscriptionsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AdminSubscriptionsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<AdminSubscriptionRowDto>>> List([FromQuery] AdminSubscriptionsQuery query)
        {
            var now = DateTime.UtcNow;

            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize is < 1 ? 20 : (query.PageSize > 200 ? 200 : query.PageSize);

            var q = (query.Q ?? "").Trim();
            var state = (query.State ?? "").Trim().ToLowerInvariant();

            var subsQ = _db.InstitutionProductSubscriptions.AsNoTracking();

            if (query.InstitutionId.HasValue)
                subsQ = subsQ.Where(s => s.InstitutionId == query.InstitutionId.Value);

            if (query.ContentProductId.HasValue)
                subsQ = subsQ.Where(s => s.ContentProductId == query.ContentProductId.Value);

            // expiring window
            if (query.ExpiringInDays.HasValue && query.ExpiringInDays.Value > 0)
            {
                var expTo = now.AddDays(query.ExpiringInDays.Value);
                subsQ = subsQ.Where(s => s.StartDate <= now && s.EndDate > now && s.EndDate <= expTo);
            }

            // derived state filter (date-based)
            if (!string.IsNullOrWhiteSpace(state))
            {
                subsQ = state switch
                {
                    "active" => subsQ.Where(s => s.StartDate <= now && s.EndDate > now),
                    "expired" => subsQ.Where(s => s.EndDate <= now),
                    "upcoming" => subsQ.Where(s => s.StartDate > now),
                    _ => subsQ
                };
            }

            // Join to Institution + ContentProduct for names (and allow q search)
            var joined = from s in subsQ
                         join i in _db.Institutions.AsNoTracking() on s.InstitutionId equals i.Id
                         join p in _db.ContentProducts.AsNoTracking() on s.ContentProductId equals p.Id
                         select new
                         {
                             SubscriptionId = s.Id,
                             s.InstitutionId,
                             InstitutionName = i.Name,
                             s.ContentProductId,
                             ContentProductName = p.Name,
                             s.StartDate,
                             s.EndDate
                         };

            if (!string.IsNullOrWhiteSpace(q))
            {
                joined = joined.Where(x =>
                    x.InstitutionName.Contains(q) ||
                    x.ContentProductName.Contains(q)
                );
            }

            // sorting
            joined = (query.Sort ?? "").ToLowerInvariant() switch
            {
                "enddate_asc" => joined.OrderBy(x => x.EndDate),
                "enddate_desc" => joined.OrderByDescending(x => x.EndDate),

                "startdate_asc" => joined.OrderBy(x => x.StartDate),
                "startdate_desc" => joined.OrderByDescending(x => x.StartDate),

                "institution_asc" => joined.OrderBy(x => x.InstitutionName),
                "institution_desc" => joined.OrderByDescending(x => x.InstitutionName),

                _ => joined.OrderByDescending(x => x.EndDate)
            };

            var total = await joined.CountAsync();

            var items = await joined
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new AdminSubscriptionRowDto(
                    SubscriptionId: x.SubscriptionId,
                    InstitutionId: x.InstitutionId,
                    InstitutionName: x.InstitutionName,
                    ContentProductId: x.ContentProductId,
                    ContentProductName: x.ContentProductName,
                    StartUtc: DateTime.SpecifyKind(x.StartDate, DateTimeKind.Utc),
                    EndUtc: DateTime.SpecifyKind(x.EndDate, DateTimeKind.Utc),
                    IsActiveNow: x.StartDate <= now && x.EndDate > now,
                    DerivedState: x.StartDate > now ? "upcoming" : (x.EndDate <= now ? "expired" : "active")
                ))
                .ToListAsync();

            return Ok(new PagedResult<AdminSubscriptionRowDto>(items, page, pageSize, total));
        }
    }
}
