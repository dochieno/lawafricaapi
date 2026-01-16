using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProfileController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public ProfileController(ApplicationDbContext db)
        {
            _db = db;
        }

        // ---------------------------------------------------------
        // GET /api/profile/me
        // ---------------------------------------------------------
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var userId = User.GetUserId();

            var user = await _db.Users
                .Include(u => u.Country)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return NotFound("User not found.");

            return Ok(new
            {
                user.Id,
                user.Username,
                user.Email,
                user.FirstName,
                user.LastName,
                user.Role,
                user.PhoneNumber,
                countryId = user.CountryId,
                countryName = user.Country?.Name,
                user.City,

                // ✅ MUST be a browser-usable URL path like:
                // /storage/ProfileImages/user_1_123.jpg OR null
                user.ProfileImageUrl,

                user.IsActive,
                user.IsEmailVerified,
                user.CreatedAt,
                user.UpdatedAt,
                user.LastLoginAt
            });
        }

        // ---------------------------------------------------------
        // PUT /api/profile/update
        // ---------------------------------------------------------
        [Authorize]
        [HttpPut("update")]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileUpdateRequest request)
        {
            var userId = User.GetUserId();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound("User not found.");

            // ---- EMAIL (with uniqueness check) ----
            if (!string.IsNullOrWhiteSpace(request.Email) &&
                !string.Equals(request.Email, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                var emailInUse = await _db.Users
                    .AnyAsync(u => u.Email == request.Email && u.Id != user.Id);

                if (emailInUse)
                    return BadRequest("Email is already in use by another account.");

                user.Email = request.Email.Trim();
                user.IsEmailVerified = false;
            }

            // ---- BASIC FIELDS ----
            if (!string.IsNullOrWhiteSpace(request.FirstName))
                user.FirstName = request.FirstName.Trim();

            if (!string.IsNullOrWhiteSpace(request.LastName))
                user.LastName = request.LastName.Trim();

            if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
                user.PhoneNumber = request.PhoneNumber.Trim();

            if (!string.IsNullOrWhiteSpace(request.City))
                user.City = request.City.Trim();

            // ✅ SECURITY FIX:
            // Do NOT allow ProfileImageUrl to be changed via UpdateProfile.
            // Profile image must be managed ONLY via /api/profile/image endpoints.
            // (This prevents bad/corrupt values like "user_1_x.jpg" or wrong casing.)

            // ---- COUNTRY ----
            if (request.CountryId.HasValue)
            {
                var exists = await _db.Countries.AnyAsync(c => c.Id == request.CountryId.Value);
                if (!exists)
                    return BadRequest("Invalid countryId.");

                user.CountryId = request.CountryId.Value;
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Profile updated successfully." });
        }

        // ---------------------------------------------------------
        // POST /api/profile/change-password
        // ---------------------------------------------------------
        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
                string.IsNullOrWhiteSpace(request.NewPassword) ||
                string.IsNullOrWhiteSpace(request.ConfirmNewPassword))
            {
                return BadRequest("All password fields are required.");
            }

            if (request.NewPassword != request.ConfirmNewPassword)
                return BadRequest("New password and confirmation do not match.");

            if (request.NewPassword.Length < 6)
                return BadRequest("New password must be at least 6 characters long.");

            var userId = User.GetUserId();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound("User not found.");

            var valid = BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash);
            if (!valid)
                return BadRequest("Current password is incorrect.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Password changed successfully." });
        }

        // ---------------------------------------------------------
        // POST /api/profile/image
        // ---------------------------------------------------------
        [Authorize]
        [HttpPost("image")]
        [RequestSizeLimit(5_000_000)]
        public async Task<IActionResult> UploadProfileImage(
            IFormFile file,
            [FromServices] IWebHostEnvironment env)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file provided.");

            if (file.Length > ProfileImageRules.MaxFileSizeBytes)
                return BadRequest("Image must be under 2MB.");

            if (!ProfileImageRules.AllowedTypes.Contains(file.ContentType))
                return BadRequest("Only JPG, PNG or WEBP allowed.");

            var userId = User.GetUserId();
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return Unauthorized();

            // ✅ IMPORTANT: folder casing EXACT (Linux is case-sensitive)
            var diskDir = Path.Combine(env.ContentRootPath, "Storage", "ProfileImages");
            Directory.CreateDirectory(diskDir);

            // ✅ Delete old image safely (supports DB old values too)
            if (!string.IsNullOrWhiteSpace(user.ProfileImageUrl))
            {
                var oldDiskPath = ResolveProfileImageDiskPath(env.ContentRootPath, user.ProfileImageUrl);
                if (!string.IsNullOrWhiteSpace(oldDiskPath) && System.IO.File.Exists(oldDiskPath))
                    System.IO.File.Delete(oldDiskPath);
            }

            var ext = Path.GetExtension(file.FileName);
            var filename = $"user_{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}";
            var fullPath = Path.Combine(diskDir, filename);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // ✅ Canonical URL stored in DB (browser-usable + correct casing)
            var urlPath = $"/storage/ProfileImages/{filename}";
            user.ProfileImageUrl = urlPath;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                // absolute for convenience
                imageUrl = $"{Request.Scheme}://{Request.Host}{urlPath}",
                // relative canonical for frontend storage
                profileImageUrl = user.ProfileImageUrl
            });
        }

        // ---------------------------------------------------------
        // DELETE /api/profile/image
        // ---------------------------------------------------------
        [Authorize]
        [HttpDelete("image")]
        public async Task<IActionResult> RemoveProfileImage([FromServices] IWebHostEnvironment env)
        {
            var userId = User.GetUserId();
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return Unauthorized();

            if (!string.IsNullOrWhiteSpace(user.ProfileImageUrl))
            {
                var diskPath = ResolveProfileImageDiskPath(env.ContentRootPath, user.ProfileImageUrl);
                if (!string.IsNullOrWhiteSpace(diskPath) && System.IO.File.Exists(diskPath))
                    System.IO.File.Delete(diskPath);

                user.ProfileImageUrl = null;
                user.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return Ok(new { message = "Profile image removed." });
        }

        // ---------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------

        /// <summary>
        /// Converts whatever is stored in DB into a real disk path.
        /// Supports:
        /// - "/storage/ProfileImages/x.jpg"
        /// - "/storage/profileimages/x.jpg" (wrong-case old values)
        /// - "Storage/ProfileImages/x.jpg"
        /// - "user_1_x.jpg" (filename-only old values)
        /// </summary>
        private static string? ResolveProfileImageDiskPath(string contentRootPath, string stored)
        {
            var s = (stored ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) return null;

            // URL form: /storage/...
            if (s.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
            {
                // remove "/storage/" -> remaining is like "ProfileImages/x.jpg" (or wrong-case)
                var rel = s.Substring("/storage/".Length).Replace('\\', '/');

                // ✅ Force canonical folder casing for disk folder
                if (rel.StartsWith("profileimages/", StringComparison.OrdinalIgnoreCase))
                    rel = "ProfileImages/" + rel.Substring("profileimages/".Length);

                return Path.Combine(contentRootPath, "Storage", rel.Replace('/', Path.DirectorySeparatorChar));
            }

            // Disk-ish form: Storage/...
            if (s.StartsWith("Storage/", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("Storage\\", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Replace('\\', '/');

                // ✅ Force canonical folder casing
                s = s.Replace("storage/profileimages/", "Storage/ProfileImages/", StringComparison.OrdinalIgnoreCase);
                s = s.Replace("storage/ProfileImages/", "Storage/ProfileImages/", StringComparison.OrdinalIgnoreCase);

                // "Storage/..." relative from content root
                return Path.Combine(contentRootPath, s.Replace('/', Path.DirectorySeparatorChar));
            }

            // Filename-only fallback
            return Path.Combine(contentRootPath, "Storage", "ProfileImages", s.TrimStart('/', '\\'));
        }
    }
}
