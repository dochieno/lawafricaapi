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

                // ✅ This should now be a browser-usable path like:
                // /storage/ProfileImages/user_1_123.jpg  OR null
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

            // NOTE: Do not let users set ProfileImageUrl from the body unless you really want that.
            // If you keep it, make sure it is sanitized.
            if (!string.IsNullOrWhiteSpace(request.ProfileImageUrl))
                user.ProfileImageUrl = request.ProfileImageUrl.Trim();

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

            // ✅ IMPORTANT: keep folder casing exactly as on disk (Linux is case-sensitive)
            var uploadsDir = Path.Combine(env.ContentRootPath, "Storage", "ProfileImages");
            Directory.CreateDirectory(uploadsDir);

            // ✅ Delete old image (safe for both old and new stored formats)
            if (!string.IsNullOrWhiteSpace(user.ProfileImageUrl))
            {
                var oldFullPath = ToPhysicalStoragePath(env, user.ProfileImageUrl);
                if (System.IO.File.Exists(oldFullPath))
                    System.IO.File.Delete(oldFullPath);
            }

            var ext = Path.GetExtension(file.FileName);
            var filename = $"user_{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}";
            var fullPath = Path.Combine(uploadsDir, filename);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // ✅ Store a frontend-usable URL path (not a physical path)
            // Must match folder casing: ProfileImages
            user.ProfileImageUrl = $"/storage/ProfileImages/{filename}";
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                imageUrl = $"{Request.Scheme}://{Request.Host}/storage/ProfileImages/{filename}",
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
                var fullPath = ToPhysicalStoragePath(env, user.ProfileImageUrl);
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);

                user.ProfileImageUrl = null;
                user.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return Ok(new { message = "Profile image removed." });
        }

        // ---------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------
        private static string ToPhysicalStoragePath(IWebHostEnvironment env, string profileImageUrl)
        {
            var v = profileImageUrl.Trim();

            // New format: "/storage/ProfileImages/xyz.jpg"
            if (v.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = v.Substring("/storage/".Length)
                    .Replace("/", Path.DirectorySeparatorChar.ToString());

                return Path.Combine(env.ContentRootPath, "Storage", relative);
            }

            // Old format: "Storage/ProfileImages/xyz.jpg"
            if (v.StartsWith("Storage/", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("Storage\\", StringComparison.OrdinalIgnoreCase))
            {
                var relative = v.Substring("Storage".Length)
                    .TrimStart('/', '\\')
                    .Replace("/", Path.DirectorySeparatorChar.ToString());

                return Path.Combine(env.ContentRootPath, "Storage", relative);
            }

            // Fallback: treat as relative to ContentRootPath
            return Path.Combine(env.ContentRootPath, v.Replace("/", Path.DirectorySeparatorChar.ToString()));
        }
    }
}
