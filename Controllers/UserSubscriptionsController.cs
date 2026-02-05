using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/subscriptions")]
    public class UserSubscriptionsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public UserSubscriptionsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // =========================
        // USER: My subscriptions
        // GET /api/subscriptions/me
        // =========================
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> MySubscriptions(CancellationToken ct)
        {
            var userId = User.GetUserId();
            var now = DateTime.UtcNow;

            var subs = await _db.UserProductSubscriptions
                .AsNoTracking()
                .Include(s => s.ContentProduct)
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.EndDate)
                .Select(s => new
                {
                    s.Id,
                    s.ContentProductId,
                    productName = s.ContentProduct.Name,
                    s.Status,
                    s.IsTrial,
                    s.StartDate,
                    s.EndDate,
                    isActiveNow = (s.Status == SubscriptionStatus.Active &&
                                   s.StartDate <= now &&
                                   s.EndDate >= now)
                })

                .ToListAsync(ct);
                var items = subs.Select(s => new
                {
                    s.Id,
                    s.ContentProductId,
                    s.productName,
                    s.Status,
                    s.IsTrial,
                    s.StartDate,
                    s.EndDate,
                    s.isActiveNow,
                    daysRemaining = s.EndDate > now
            ? (int)Math.Ceiling((s.EndDate - now).TotalDays)
            : 0
                }).ToList();


            return Ok(new { userId, now, items = subs });
        }

        // ======================================================
        // ADMIN: list subscriptions (monitoring)
        // GET /api/subscriptions/admin/list?status=Active&isTrial=true&page=1&pageSize=50&q=damaris
        // ======================================================
        [Authorize(Roles = "Admin")]
        [HttpGet("admin/list")]
        public async Task<IActionResult> AdminList(
            [FromQuery] SubscriptionStatus? status = null,
            [FromQuery] bool? isTrial = null,
            [FromQuery] bool includeExpiredWindows = false,
            [FromQuery] string? q = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            EnsureGlobalAdmin(); // uses JWT claim (fast)

            var now = DateTime.UtcNow;

            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 200);

            var query = _db.UserProductSubscriptions
                .AsNoTracking()
                .Include(s => s.User)
                .Include(s => s.ContentProduct)
                .AsQueryable();

            if (status.HasValue)
                query = query.Where(s => s.Status == status.Value);

            if (isTrial.HasValue)
                query = query.Where(s => s.IsTrial == isTrial.Value);

            // By default, show "currently active window" subscriptions only
            // Unless includeExpiredWindows=true
            if (!includeExpiredWindows)
            {
                query = query.Where(s =>
                    s.Status == SubscriptionStatus.Active &&
                    s.StartDate <= now &&
                    s.EndDate >= now);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(s =>
                    s.User.Username.Contains(q) ||
                    s.User.Email.Contains(q) ||
                    s.User.PhoneNumber.Contains(q) ||
                    s.ContentProduct.Name.Contains(q));
            }

            var total = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(s => s.EndDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.Id,
                    s.UserId,
                    user = new
                    {
                        s.User.Username,
                        s.User.Email,
                        s.User.PhoneNumber,
                        s.User.UserType,
                        s.User.InstitutionId
                    },
                    s.ContentProductId,
                    productName = s.ContentProduct.Name,
                    s.Status,
                    s.IsTrial,
                    s.StartDate,
                    s.EndDate,
                    isActiveNow = (s.Status == SubscriptionStatus.Active && s.StartDate <= now && s.EndDate >= now),
                    grantedByUserId = s.GrantedByUserId
                })
                .ToListAsync(ct);

            return Ok(new
            {
                now,
                page,
                pageSize,
                total,
                items
            });
        }

        // =========================
        // Helpers
        // =========================
        private void EnsureGlobalAdmin()
        {
            // JWT claim you already issue: "isGlobalAdmin" = "true"/"false"
            var raw = User.FindFirstValue("isGlobalAdmin");

            if (!string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Global admin privileges required.");
        }
    }
}
