// Controllers/SecurityController.cs
using LawAfrica.API.Data;
using LawAfrica.API.Models.DTOs.Security;
using LawAfrica.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SecurityController : ControllerBase
    {
        private readonly AuthService _auth;
        private readonly ApplicationDbContext _db;

        public SecurityController(AuthService auth, ApplicationDbContext db)
        {
            _auth = auth;
            _db = db;
        }

        // =========================================================
        // Helper: robust userId extraction from JWT
        // =========================================================
        private bool TryGetUserId(out int userId)
        {
            userId = 0;

            var raw =
                User.FindFirst("userId")?.Value ??
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                User.FindFirst("sub")?.Value ??
                User.FindFirst("nameid")?.Value ??
                User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

            return int.TryParse(raw, out userId);
        }

        // =========================================================
        // 0) ONBOARDING FLOW (NO JWT)
        // =========================================================
        [AllowAnonymous]
        [HttpPost("verify-2fa-setup")]
        public async Task<IActionResult> VerifyTwoFactorSetup([FromBody] VerifyTwoFactorSetupRequest request)
        {
            var success = await _auth.VerifyTwoFactorSetupByTokenAsync(request.SetupToken, request.Code);

            return success
                ? Ok(new { message = "2FA Enabled!" })
                : BadRequest("Invalid/expired setup token or invalid code.");
        }

        /// <summary>
        /// Resend setup email for onboarding when user doesn't have JWT yet.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("resend-2fa-setup")]
        public async Task<IActionResult> ResendTwoFactorSetup([FromBody] ResendTwoFactorSetupRequest request)
        {
            var result = await _auth.ResendTwoFactorSetupAsync(request.Username, request.Password);

            if (result == null)
                return Ok(new { message = "If the account exists and credentials are correct, a new 2FA setup email has been sent." });

            return Ok(new
            {
                message = "2FA setup email resent.",
                setupToken = result.SetupToken,
                setupTokenExpiryUtc = result.SetupTokenExpiryUtc
            });
        }

        // =========================================================
        // 1) AUTHENTICATED USER MANAGEMENT (JWT REQUIRED)
        // =========================================================
        [Authorize]
        [HttpPost("enable-2fa")]
        public async Task<IActionResult> Enable2FA()
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized("Token missing userId claim.");

            var response = await _auth.EnableTwoFactorAuthAsync(userId);
            return Ok(response);
        }

        [Authorize]
        [HttpGet("status")]
        public async Task<IActionResult> SecurityStatus()
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized("Token missing userId claim.");

            var status = await _auth.GetSecurityStatusAsync(userId);
            if (status == null)
                return NotFound();

            return Ok(status);
        }

        [Authorize]
        [HttpPost("disable-2fa")]
        public async Task<IActionResult> Disable2FA()
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized("Token missing userId claim.");

            var success = await _auth.DisableTwoFactorAsync(userId);
            if (!success)
                return BadRequest("Unable to disable 2FA.");

            return Ok(new { message = "Two-factor authentication disabled." });
        }

        [Authorize]
        [HttpPost("regenerate-2fa")]
        public async Task<IActionResult> Regenerate2FA()
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized("Token missing userId claim.");

            var response = await _auth.RegenerateTwoFactorAuthAsync(userId);
            return Ok(response);
        }

        // =========================================================
        // ✅ NEW: Verify password WITHOUT triggering /auth/login (no TOTP)
        // Used for: Reset PIN securely inside an already-authenticated session.
        // =========================================================
        public class VerifyPasswordRequest
        {
            public string Password { get; set; } = string.Empty;
        }

        [Authorize]
        [HttpPost("verify-password")]
        public async Task<IActionResult> VerifyPassword([FromBody] VerifyPasswordRequest request)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized("Token missing userId claim.");

            var password = (request?.Password ?? "").Trim();
            if (string.IsNullOrWhiteSpace(password))
                return BadRequest("Password is required.");

            // Minimal DB query
            var user = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.PasswordHash, u.IsActive })
                .FirstOrDefaultAsync();

            if (user == null)
                return Unauthorized("User not found.");

            if (!user.IsActive)
                return BadRequest("Account is disabled.");

            if (string.IsNullOrWhiteSpace(user.PasswordHash))
                return BadRequest("Password is not set.");

            // Your system uses BCrypt
            var ok = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);

            if (!ok)
                return BadRequest("Incorrect password.");

            return Ok(new { ok = true });
        }

        // =========================================================
        // 2) DEPRECATED ENDPOINT (REMOVE/DO NOT USE)
        // =========================================================
        [Authorize]
        [HttpPost("verify-2fa")]
        public IActionResult Verify2FA_Deprecated()
        {
            return StatusCode(410, "Deprecated. Use /api/security/verify-2fa-setup for initial setup.");
        }
    }
}
