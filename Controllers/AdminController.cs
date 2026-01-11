using LawAfrica.API.Data;
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


        // ---------------- GET ALL USERS (ADMIN ONLY) ----------------
        [Authorize(Policy = "RequireAdmin")]
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _db.Users
                .Include(u => u.Country)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.Email,
                    u.Role,
                    u.IsActive,
                    u.IsEmailVerified,
                    u.CreatedAt,
                    Country = u.Country != null ? u.Country.Name : null
                })
                .ToListAsync();

            return Ok(users);
        }

        // Request model for changing user role
        public class ChangeRoleRequest
        {
            public string NewRole { get; set; } = string.Empty;
        }

        // ---------------- CHANGE ROLE (ADMIN ONLY) ----------------
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

        //Admin can regenerate 2FA for any user
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

        // ---------------- GET LOGIN AUDITS (ADMIN ONLY) ----------------
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
        // ---------------- UNLOCK USER ACCOUNT (ADMIN ONLY) ----------------

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

        // ---------------- GET LOCKED USERS (ADMIN ONLY) ----------------
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

        // ---------------- GET SUSPICIOUS IPs (ADMIN ONLY) ----------------
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

        // ---------------- GET USER SECURITY DETAILS (ADMIN ONLY) ----------------
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
