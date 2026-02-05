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

            // NOTE: you currently return "items = subs" (not "items")
            // Keeping your existing behavior to avoid breaking any clients.
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
                        s.User.FirstName,
                        s.User.LastName,
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

        // ======================================================
        // ADMIN: suspend a user subscription (Global Admin)
        // POST /api/subscriptions/admin/{id}/suspend
        // Body: { "notes": "optional" }
        // ======================================================
        public class SubscriptionAdminActionRequest
        {
            public string? Notes { get; set; }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("admin/{id:int}/suspend")]
        public async Task<IActionResult> Suspend(int id, [FromBody] SubscriptionAdminActionRequest? body, CancellationToken ct)
        {
            EnsureGlobalAdmin();

            var sub = await _db.UserProductSubscriptions
                .Include(s => s.User)
                .Include(s => s.ContentProduct)
                .FirstOrDefaultAsync(s => s.Id == id, ct);

            if (sub == null)
                return NotFound(new { message = "Subscription not found." });

            if (sub.Status == SubscriptionStatus.Suspended)
            {
                return Ok(new
                {
                    message = "Subscription is already suspended.",
                    id = sub.Id,
                    status = sub.Status
                });
            }

            // If you want stricter rules, add them here (e.g. cannot suspend Pending)
            sub.Status = SubscriptionStatus.Suspended;

            // Optional: if you later add audit table, record body.Notes + performedBy here

            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                message = "Subscription suspended.",
                id = sub.Id,
                userId = sub.UserId,
                productName = sub.ContentProduct?.Name,
                status = sub.Status
            });
        }

        // ======================================================
        // ADMIN: unsuspend a user subscription (Global Admin)
        // POST /api/subscriptions/admin/{id}/unsuspend
        // Body: { "notes": "optional" }
        // ======================================================
        [Authorize(Roles = "Admin")]
        [HttpPost("admin/{id:int}/unsuspend")]
        public async Task<IActionResult> Unsuspend(int id, [FromBody] SubscriptionAdminActionRequest? body, CancellationToken ct)
        {
            EnsureGlobalAdmin();

            var now = DateTime.UtcNow;

            var sub = await _db.UserProductSubscriptions
                .Include(s => s.User)
                .Include(s => s.ContentProduct)
                .FirstOrDefaultAsync(s => s.Id == id, ct);

            if (sub == null)
                return NotFound(new { message = "Subscription not found." });

            if (sub.Status != SubscriptionStatus.Suspended)
            {
                return Ok(new
                {
                    message = "Subscription is not suspended.",
                    id = sub.Id,
                    status = sub.Status
                });
            }

            // ✅ Practical rule:
            // If the window is still valid, restore to Active.
            // If it expired, restore to Expired (or keep Active if you prefer).
            var isWindowValid = sub.StartDate <= now && sub.EndDate >= now;

            sub.Status = isWindowValid ? SubscriptionStatus.Active : SubscriptionStatus.Expired;

            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                message = isWindowValid ? "Subscription unsuspended (Active)." : "Subscription unsuspended (Expired window).",
                id = sub.Id,
                userId = sub.UserId,
                productName = sub.ContentProduct?.Name,
                status = sub.Status,
                windowValidNow = isWindowValid
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
