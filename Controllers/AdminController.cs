using LawAfrica.API.Authorization.Policies;
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Institutions;
using LawAfrica.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdmin")] // keeps your Program.cs policy
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

            // Memberships (1 per user+institution)
            var membershipsQ = _db.InstitutionMemberships.AsNoTracking();

            // Join presence + membership (LEFT JOIN both)
            var joined =
                from u in usersQ
                join p in presenceAgg on u.Id equals p.UserId into pj
                from p in pj.DefaultIfEmpty()
                join m in membershipsQ
                    on new { UserId = u.Id, InstitutionId = (int?)u.InstitutionId }
                    equals new { UserId = m.UserId, InstitutionId = (int?)m.InstitutionId }
                    into mj
                from m in mj.DefaultIfEmpty()
                select new
                {
                    User = u,
                    LastSeenAtUtc = (DateTime?)p.LastSeenAtUtc,

                    MembershipId = (int?)m.Id,
                    MembershipStatus = (MembershipStatus?)m.Status,
                    MemberType = (InstitutionMemberType?)m.MemberType,
                    ReferenceNumber = m.ReferenceNumber,
                    MembershipIsActive = (bool?)m.IsActive,
                    MembershipApprovedAt = (DateTime?)m.ApprovedAt,

                    IsInstitutionAdmin = m != null
                        && m.MemberType == InstitutionMemberType.InstitutionAdmin
                        && m.Status == MembershipStatus.Approved
                        && m.IsActive
                };

            // Online filter
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
                    IsOnline = x.LastSeenAtUtc != null && x.LastSeenAtUtc >= onlineCutoff,

                    // ✅ Membership fields required by frontend approval/actions
                    MembershipId = x.MembershipId,
                    MembershipStatus = x.MembershipStatus,
                    MemberType = x.MemberType,
                    ReferenceNumber = x.ReferenceNumber,
                    MembershipIsActive = x.MembershipIsActive,
                    MembershipApprovedAt = x.MembershipApprovedAt,

                    // ✅ Computed
                    IsInstitutionAdmin = x.IsInstitutionAdmin
                })
                .ToListAsync();

            // Summary counts (fast + stable)
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

            var onlineCount = await presenceAgg.CountAsync(p => p.LastSeenAtUtc >= onlineCutoff);

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
        // USERS - ACTIVATE / DEACTIVATE
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
        // USERS - LOCK / UNLOCK
        // PUT /api/admin/users/{id}/lock
        // =========================================================
        public class SetLockRequest
        {
            public bool Locked { get; set; }
            public int? Minutes { get; set; }
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
                    : (60 * 24 * 365);

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
        // USERS - CHANGE ROLE
        // POST /api/admin/users/{id}/role
        // Body: { newRole: "Admin"|"User" }
        // =========================================================
        public class ChangeRoleRequest
        {
            public string NewRole { get; set; } = string.Empty;
        }

        [HttpPost("users/{id}/role")]
        public async Task<IActionResult> ChangeRole(int id, [FromBody] ChangeRoleRequest request)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound("User not found.");

            // ✅ Rule: Institution users can NEVER be system Admin
            if (user.InstitutionId != null &&
                request.NewRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Institution users cannot be promoted to system Admin.");
            }

            if (user.IsGlobalAdmin &&
                request.NewRole.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("You cannot demote a global admin account.");
            }

            var validRoles = new[] { "Admin", "User" };
            if (!validRoles.Contains(request.NewRole))
                return BadRequest("Invalid role. Allowed: Admin, User");

            user.Role = request.NewRole;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new { message = $"User '{user.Username}' role changed to '{user.Role}'." });
        }

        // =========================================================
        // USERS - REGENERATE 2FA
        // POST /api/admin/users/{id}/regenerate-2fa
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
        // LOGIN AUDITS
        // GET /api/admin/login-audits
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
        // UNLOCK USER ACCOUNT
        // POST /api/admin/users/{id}/unlock
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
        // GET LOCKED USERS
        // GET /api/admin/security/locked-users
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
        // GET SUSPICIOUS IPs
        // GET /api/admin/security/suspicious-ips
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
        // GET USER SECURITY DETAILS
        // GET /api/admin/security/users/{userId}
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


        //Additions:

        // =========================================================
        // USERS - SET / UNSET GLOBAL ADMIN
        // PUT /api/admin/users/{id}/global-admin
        // Body: { isGlobalAdmin: true|false }
        // Notes:
        // - Only a CURRENT Global Admin can change this flag
        // - Promoting always ensures Role="Admin"
        // - Prevent self-demotion for safety
        // - Institution users cannot become Global Admin (optional but recommended)
        // =========================================================
        public class SetGlobalAdminRequest
        {
            public bool IsGlobalAdmin { get; set; }
        }

        private bool CurrentSessionIsGlobalAdmin()
        {
            // Claim is set by your backend as "isGlobalAdmin": "true"/"false"
            var claim = User?.FindFirst("isGlobalAdmin")?.Value;
            return string.Equals(claim, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(claim, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(claim, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private int? CurrentSessionUserId()
        {
            var v =
                User?.FindFirst("userId")?.Value ??
                User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                User?.FindFirst("sub")?.Value;

            return int.TryParse(v, out var id) ? id : (int?)null;
        }

        [HttpPut("users/{id}/global-admin")]
        public async Task<IActionResult> SetGlobalAdmin(int id, [FromBody] SetGlobalAdminRequest req)
        {
            // ✅ Only Global Admin can promote/demote Global Admin
            if (!CurrentSessionIsGlobalAdmin())
                return Forbid("Only a Global Admin can perform this action.");

            var meId = CurrentSessionUserId();
            if (meId.HasValue && meId.Value == id && req.IsGlobalAdmin == false)
                return BadRequest("You cannot remove your own global admin access.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound("User not found.");

            // ✅ Recommended rule (matches your other rules):
            // Institution users should NOT become system/global admins
            if (user.InstitutionId != null && req.IsGlobalAdmin)
                return BadRequest("Institution users cannot be promoted to Global Admin.");

            // Apply change
            user.IsGlobalAdmin = req.IsGlobalAdmin;

            // Your requirement: when promoting to Global Admin, also set Role=Admin
            if (req.IsGlobalAdmin)
            {
                user.Role = "Admin";
                user.IsActive = true; // optional safety, remove if you don't want
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = req.IsGlobalAdmin ? "Global Admin granted." : "Global Admin removed.",
                userId = user.Id,
                user.Username,
                user.Email,
                user.Role,
                user.IsGlobalAdmin
            });
        }

    }
}
