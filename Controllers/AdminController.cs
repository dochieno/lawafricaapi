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
    [Authorize(Roles = "Admin")]
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
        // GET /api/admin/users?q=&type=&status=&online=&institutionId=&page=&pageSize=
        //
        // type: all | public | institution
        // status: all | active | inactive | locked
        // online: true | false
        // =========================================================
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
            var onlineCutoff = now.AddMinutes(-5);

            // Aggregate presences: 1 row per user
            var presenceAgg =
                _db.UserPresences
                  .AsNoTracking()
                  .GroupBy(p => p.UserId)
                  .Select(g => new
                  {
                      UserId = g.Key,
                      LastSeenAtUtc = g.Max(x => x.LastSeenAtUtc)
                  });

            // Base users query
            var usersQ = _db.Users
                .AsNoTracking()
                .Include(u => u.Institution)
                .Include(u => u.Country)
                .AsQueryable();

            // Search (Postgres-friendly, case-insensitive)
            if (!string.IsNullOrWhiteSpace(q))
            {
                var s = q.Trim();
                usersQ = usersQ.Where(u =>
                    EF.Functions.ILike(u.Username, $"%{s}%") ||
                    EF.Functions.ILike(u.Email, $"%{s}%") ||
                    (u.FirstName != null && EF.Functions.ILike(u.FirstName, $"%{s}%")) ||
                    (u.LastName != null && EF.Functions.ILike(u.LastName, $"%{s}%")));
            }

            // Type filter
            var t = (type ?? "all").Trim().ToLower();
            if (t == "public")
            {
                usersQ = usersQ.Where(u => u.InstitutionId == null && u.UserType == UserType.Public);
            }
            else if (t == "institution")
            {
                usersQ = usersQ.Where(u => u.InstitutionId != null);
            }

            // Institution filter
            if (institutionId.HasValue && institutionId.Value > 0)
            {
                usersQ = usersQ.Where(u => u.InstitutionId == institutionId.Value);
            }

            // Status filter
            var st = (status ?? "all").Trim().ToLower();
            if (st == "active")
            {
                usersQ = usersQ.Where(u => u.IsActive == true);
            }
            else if (st == "inactive")
            {
                usersQ = usersQ.Where(u => u.IsActive == false);
            }
            else if (st == "locked")
            {
                usersQ = usersQ.Where(u => u.LockoutEndAt != null && u.LockoutEndAt > now);
            }

            // Join presence (so we can filter by online efficiently)
            var joined =
                from u in usersQ
                join p in presenceAgg on u.Id equals p.UserId into pj
                from p in pj.DefaultIfEmpty()
                select new
                {
                    User = u,
                    LastSeenAtUtc = (DateTime?)p.LastSeenAtUtc
                };

            if (online.HasValue)
            {
                if (online.Value)
                {
                    joined = joined.Where(x => x.LastSeenAtUtc != null && x.LastSeenAtUtc >= onlineCutoff);
                }
                else
                {
                    joined = joined.Where(x => x.LastSeenAtUtc == null || x.LastSeenAtUtc < onlineCutoff);
                }
            }

            var total = await joined.CountAsync();

            // page items
            var items = await joined
                .OrderByDescending(x => x.User.LastLoginAt ?? x.User.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.User.Id,
                    x.User.Username,
                    x.User.Email,
                    Name = (x.User.FirstName ?? "") + (string.IsNullOrWhiteSpace(x.User.LastName) ? "" : " " + x.User.LastName),

                    x.User.Role,
                    x.User.UserType,
                    x.User.IsGlobalAdmin,

                    x.User.IsActive,
                    x.User.IsApproved,
                    x.User.IsEmailVerified,

                    x.User.InstitutionId,
                    InstitutionName = x.User.Institution != null ? x.User.Institution.Name : null,

                    Country = x.User.Country != null ? x.User.Country.Name : null,

                    x.User.CreatedAt,
                    x.User.LastLoginAt,
                    x.User.UpdatedAt,

                    x.User.LockoutEndAt,
                    x.User.FailedLoginAttempts,

                    LastSeenAtUtc = x.LastSeenAtUtc,
                    IsOnline = x.LastSeenAtUtc != null && x.LastSeenAtUtc >= onlineCutoff
                })
                .ToListAsync();

            // Summary counts for UI chips (public/institution/online/locked/active)
            // Keep it lightweight with a few targeted counts.
            var baseUsers = _db.Users.AsNoTracking().AsQueryable();

            var activePublicCount = await baseUsers.CountAsync(u =>
                u.IsActive &&
                u.InstitutionId == null &&
                u.UserType == UserType.Public);

            var activeInstitutionCount = await baseUsers.CountAsync(u =>
                u.IsActive &&
                u.InstitutionId != null);

            var lockedCount = await baseUsers.CountAsync(u =>
                u.LockoutEndAt != null && u.LockoutEndAt > now);

            var onlineCount =
                await presenceAgg.CountAsync(p => p.LastSeenAtUtc >= onlineCutoff);

            return Ok(new
            {
                page,
                pageSize,
                total,
                summary = new
                {
                    activePublic = activePublicCount,
                    activeInstitution = activeInstitutionCount,
                    locked = lockedCount,
                    online = onlineCount
                },
                items
            });
        }

        // =========================================================
        // USERS - ACTIVATE / DEACTIVATE (ADMIN ONLY)
        // PUT /api/admin/users/{id}/active
        // =========================================================
        public class SetActiveRequest
        {
            public bool IsActive { get; set; }
        }


        [HttpPut("users/{id}/active")]
        public async Task<IActionResult> SetUserActive(int id, [FromBody] SetActiveRequest req)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound("User not found.");

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
        // PUT /api/admin/users/{id}/lock
        // =========================================================
        public class SetLockRequest
        {
            public bool Locked { get; set; }
            public int? Minutes { get; set; } // if locking
        }

        [HttpPut("users/{id}/lock")]
        public async Task<IActionResult> SetUserLock(int id, [FromBody] SetLockRequest req)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound("User not found.");

            if (user.IsGlobalAdmin && req.Locked)
                return BadRequest("You cannot lock a global admin account.");

            if (req.Locked)
            {
                var mins = req.Minutes.HasValue && req.Minutes.Value > 0
                    ? req.Minutes.Value
                    : (60 * 24 * 365); // default: 1 year

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

            return Ok(new { message = $"User '{user.Username}' role changed to '{user.Role}'." });
        }

        // =========================================================
        // KEEP: REGENERATE 2FA (ADMIN ONLY)
        // =========================================================

        [HttpPost("users/{id}/regenerate-2fa")]
        public async Task<IActionResult> RegenerateUser2FA(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound("User not found.");

            await _auth.RegenerateTwoFactorAuthAsync(id);

            return Ok(new { message = $"2FA reset for user '{user.Username}'. New QR code sent via email." });
        }

        // =========================================================
        // KEEP: LOGIN AUDITS (ADMIN ONLY)
        // =========================================================

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
        // =========================================================

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
