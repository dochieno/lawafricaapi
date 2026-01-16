using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly AuthService _auth;

        public AdminController(ApplicationDbContext db, AuthService auth)
        {
            _db = db;
            _auth = auth;
        }

        // =========================================================
        // USERS - LIST (ADMIN ONLY)
        // Upgraded: search + filters + pagination + online
        // =========================================================
        // GET /api/admin/users?q=&type=&status=&online=&institutionId=&page=&pageSize=
        //
        // type: all | public | institution
        // status: all | active | inactive | locked
        // online: true | false
        [Authorize(Policy = "RequireAdmin")]
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers(
            [FromQuery] string? q = null,
            [FromQuery] string? type = "all",
            [FromQuery] string? status = "all",
            [FromQuery] bool? online = null,
            [FromQuery] int? institutionId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 200);

            var now = DateTime.UtcNow;
            var onlineCutoff = now.AddMinutes(-5); // online = seen within last 5 minutes

            var query = _db.Users
                .AsNoTracking()
                .Include(u => u.Institution)
                .Include(u => u.Country)
                .AsQueryable();

            // Search
            if (!string.IsNullOrWhiteSpace(q))
            {
                var s = q.Trim().ToLower();
                query = query.Where(u =>
                    u.Username.ToLower().Contains(s) ||
                    u.Email.ToLower().Contains(s) ||
                    (u.FirstName != null && u.FirstName.ToLower().Contains(s)) ||
                    (u.LastName != null && u.LastName.ToLower().Contains(s)));
            }

            // Type filter
            var t = (type ?? "all").Trim().ToLower();
            if (t == "public")
            {
                query = query.Where(u => u.InstitutionId == null && u.UserType == UserType.Public);
            }
            else if (t == "institution")
            {
                query = query.Where(u => u.InstitutionId != null);
            }

            // Institution filter
            if (institutionId.HasValue && institutionId.Value > 0)
            {
                query = query.Where(u => u.InstitutionId == institutionId.Value);
            }

            // Status filter
            var st = (status ?? "all").Trim().ToLower();
            if (st == "active")
            {
                query = query.Where(u => u.IsActive == true);
            }
            else if (st == "inactive")
            {
                query = query.Where(u => u.IsActive == false);
            }
            else if (st == "locked")
            {
                query = query.Where(u => u.LockoutEndAt != null && u.LockoutEndAt > now);
            }

            // Online filter (requires UserPresences table)
            // If table doesn't exist yet, you'll add it below.
            if (online.HasValue)
            {
                if (online.Value)
                {
                    query = query.Where(u =>
                        _db.UserPresences.Any(p => p.UserId == u.Id && p.LastSeenAtUtc >= onlineCutoff));
                }
                else
                {
                    query = query.Where(u =>
                        !_db.UserPresences.Any(p => p.UserId == u.Id && p.LastSeenAtUtc >= onlineCutoff));
                }
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(u => u.LastLoginAt ?? u.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.Email,
                    Name = (u.FirstName ?? "") + (string.IsNullOrWhiteSpace(u.LastName) ? "" : " " + u.LastName),
                    u.Role,
                    u.UserType,
                    u.IsGlobalAdmin,

                    u.IsActive,
                    u.IsApproved,
                    u.IsEmailVerified,

                    u.InstitutionId,
                    InstitutionName = u.Institution != null ? u.Institution.Name : null,

                    Country = u.Country != null ? u.Country.Name : null,

                    u.CreatedAt,
                    u.LastLoginAt,
                    u.UpdatedAt,

                    u.LockoutEndAt,
                    u.FailedLoginAttempts,

                    // online derived
                    IsOnline = _db.UserPresences.Any(p => p.UserId == u.Id && p.LastSeenAtUtc >= onlineCutoff),
                    LastSeenAtUtc = _db.UserPresences
                        .Where(p => p.UserId == u.Id)
                        .Select(p => (DateTime?)p.LastSeenAtUtc)
                        .FirstOrDefault()
                })
                .ToListAsync();

                return Ok(new
                {
                    page,
                    pageSize,
                    total,
                    items
                });
        }

        // =========================================================
        // USERS - ACTIVATE / DEACTIVATE (ADMIN ONLY)
        // =========================================================
        public class SetActiveRequest
        {
            public bool IsActive { get; set; }
        }

        // PUT /api/admin/users/{id}/active
        [Authorize(Policy = "RequireAdmin")]
        [HttpPut("users/{id}/active")]
        public async Task<IActionResult> SetUserActive(int id, [FromBody] SetActiveRequest req)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound("User not found.");

            // Optional safety: don't allow deactivating global admin
            if (user.IsGlobalAdmin && req.IsActive == false)
                return BadRequest("You cannot deactivate a global admin account.");

            user.IsActive = req.IsActive;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = req.IsActive ? "User activated." : "User deactivated.",
                userId = user.Id,
                user.IsActive
            });
        }

        // =========================================================
        // USERS - BLOCK / UNBLOCK SIGN-IN (LOCKOUT) (ADMIN ONLY)
        // =========================================================
        public class SetLockRequest
        {
            // true => lock, false => unlock
            public bool Locked { get; set; }

            // If locking: minutes from now. If null => 1 year.
            public int? Minutes { get; set; }
        }

        // PUT /api/admin/users/{id}/lock
        [Authorize(Policy = "RequireAdmin")]
        [HttpPut("users/{id}/lock")]
        public async Task<IActionResult> SetUserLock(int id, [FromBody] SetLockRequest req)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound("User not found.");

            if (user.IsGlobalAdmin && req.Locked == true)
                return BadRequest("You cannot lock a global admin account.");

            if (req.Locked)
            {
                var mins = req.Minutes.HasValue && req.Minutes.Value > 0 ? req.Minutes.Value : (60 * 24 * 365);
                user.LockoutEndAt = DateTime.UtcNow.AddMinutes(mins);
            }
            else
            {
                user.LockoutEndAt = null;
                user.FailedLoginAttempts = 0;
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = req.Locked ? "User sign-in blocked." : "User unblocked.",
                userId = user.Id,
                user.LockoutEndAt,
                user.FailedLoginAttempts
            });
        }

        // =========================================================
        // KEEP: CHANGE ROLE (ADMIN ONLY)
        // =========================================================
        public class ChangeRoleRequest
        {
            public string NewRole { get; set; } = string.Empty;
        }

        [Authorize(Policy = "RequireAdmin")]
        [HttpPost("users/{id}/role")]
        public async Task<IActionResult> ChangeRole(int id, ChangeRoleRequest request)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound("User not found.");

            var validRoles = new[] { "Admin", "User" };
            if (!validRoles.Contains(request.NewRole))
                return BadRequest("Invalid role. Allowed: Admin, User");

            user.Role = request.NewRole;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = $"User '{user.Username}' role changed to '{user.Role}'."
            });
        }

        // =========================================================
        // KEEP: REGENERATE 2FA (ADMIN ONLY)
        // =========================================================
        [Authorize(Policy = "RequireAdmin")]
        [HttpPost("users/{id}/regenerate-2fa")]
        public async Task<IActionResult> RegenerateUser2FA(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound("User not found.");

            await _auth.RegenerateTwoFactorAuthAsync(id);

            return Ok(new
            {
                message = $"2FA reset for user '{user.Username}'. New QR code sent via email."
            });
        }

        // =========================================================
        // KEEP: LOGIN AUDITS (ADMIN ONLY)
        // =========================================================
        [Authorize(Policy = "RequireAdmin")]
        [HttpGet("login-audits")]
        public async Task<IActionResult> GetLoginAudits()
        {
            var audits = await _db.LoginAudits
                .OrderByDescending(a => a.LoggedInAt)
                .Take(500)
                .Select(a => new
                {
                    a.UserId,
                    a.IsSuccessful,
                    a.FailureReason,
                    a.IpAddress,
                    a.UserAgent,
                    a.LoggedInAt
                })
                .ToListAsync();

            return Ok(audits);
        }

        // =========================================================
        // KEEP: UNLOCK USER ACCOUNT (ADMIN ONLY)
        // (kept for backward compatibility)
        // =========================================================
        [Authorize(Policy = "RequireAdmin")]
        [HttpPost("users/{id}/unlock")]
        public async Task<IActionResult> UnlockUser(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            user.LockoutEndAt = null;
            user.FailedLoginAttempts = 0;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new { message = "User unlocked." });
        }

        // =========================================================
        // KEEP: GET LOCKED USERS (ADMIN ONLY)
        // =========================================================
        [Authorize(Policy = "RequireAdmin")]
        [HttpGet("security/locked-users")]
        public async Task<IActionResult> GetLockedUsers()
        {
            var now = DateTime.UtcNow;

            var users = await _db.Users
                .Where(u => u.LockoutEndAt != null && u.LockoutEndAt > now)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.Email,
                    u.LockoutEndAt,
                    u.FailedLoginAttempts
                })
                .ToListAsync();

            return Ok(users);
        }

        // =========================================================
        // KEEP: GET SUSPICIOUS IPs (ADMIN ONLY)
        // =========================================================
        [Authorize(Policy = "RequireAdmin")]
        [HttpGet("security/suspicious-ips")]
        public async Task<IActionResult> GetSuspiciousIps()
        {
            var ips = await _db.LoginAudits
                .Where(l => !l.IsSuccessful && l.IpAddress != null)
                .GroupBy(l => l.IpAddress!)
                .Select(g => new
                {
                    IpAddress = g.Key,
                    FailedAttempts = g.Count(),
                    LastAttempt = g.Max(x => x.LoggedInAt)
                })
                .OrderByDescending(x => x.FailedAttempts)
                .Take(20)
                .ToListAsync();

            return Ok(ips);
        }

        // =========================================================
        // KEEP: GET USER SECURITY DETAILS (ADMIN ONLY)
        // =========================================================
        [Authorize(Policy = "RequireAdmin")]
        [HttpGet("security/users/{userId}")]
        public async Task<IActionResult> GetUserSecurity(int userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null)
                return NotFound();

            var audits = await _db.LoginAudits
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.LoggedInAt)
                .Take(50)
                .Select(l => new
                {
                    l.IsSuccessful,
                    l.FailureReason,
                    l.IpAddress,
                    l.UserAgent,
                    l.LoggedInAt
                })
                .ToListAsync();

            return Ok(new
            {
                user.Id,
                user.Username,
                user.Email,
                user.IsActive,
                user.LockoutEndAt,
                user.FailedLoginAttempts,
                RecentLogins = audits
            });
        }
    }
}
