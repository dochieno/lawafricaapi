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
        private readonly IConfiguration _config;

        public ProfileController(ApplicationDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
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

            // Optional: normalize legacy values without writing to DB
            var normalized = NormalizeProfileImageUrl(user.ProfileImageUrl);

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

                ProfileImageUrl = normalized, // ✅ always safe for browser

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

            if (!string.IsNullOrWhiteSpace(request.FirstName))
                user.FirstName = request.FirstName.Trim();

            if (!string.IsNullOrWhiteSpace(request.LastName))
                user.LastName = request.LastName.Trim();

            if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
                user.PhoneNumber = request.PhoneNumber.Trim();

            if (!string.IsNullOrWhiteSpace(request.City))
                user.City = request.City.Trim();

            // ✅ DO NOT allow ProfileImageUrl from body

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
        // POST /api/profile/image
        // ---------------------------------------------------------
        [Authorize]
        [HttpPost("image")]
        [RequestSizeLimit(5_000_000)]
        public async Task<IActionResult> UploadProfileImage([FromForm] IFormFile file)
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

            // ✅ CRITICAL: Save where /storage/** serves from (Render: STORAGE_ROOT)
            var storageRoot = GetStorageRoot();
            var diskDir = Path.Combine(storageRoot, "ProfileImages");
            Directory.CreateDirectory(diskDir);

            // ✅ delete old
            if (!string.IsNullOrWhiteSpace(user.ProfileImageUrl))
            {
                var oldDiskPath = ResolveProfileImageDiskPath(storageRoot, user.ProfileImageUrl);
                if (!string.IsNullOrWhiteSpace(oldDiskPath) && System.IO.File.Exists(oldDiskPath))
                    System.IO.File.Delete(oldDiskPath);
            }

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";

            var filename = $"user_{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}";
            var fullPath = Path.Combine(diskDir, filename);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // ✅ canonical url path (must match Program.cs "/storage" mapping)
            var urlPath = $"/storage/ProfileImages/{filename}";
            user.ProfileImageUrl = urlPath;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                imageUrl = $"{Request.Scheme}://{Request.Host}{urlPath}",
                profileImageUrl = user.ProfileImageUrl
            });
        }

        // ---------------------------------------------------------
        // DELETE /api/profile/image
        // ---------------------------------------------------------
        [Authorize]
        [HttpDelete("image")]
        public async Task<IActionResult> RemoveProfileImage()
        {
            var userId = User.GetUserId();
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return Unauthorized();

            var storageRoot = GetStorageRoot();

            if (!string.IsNullOrWhiteSpace(user.ProfileImageUrl))
            {
                var diskPath = ResolveProfileImageDiskPath(storageRoot, user.ProfileImageUrl);
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
        private string GetStorageRoot()
        {
            // Must match Program.cs
            var root = _config["STORAGE_ROOT"];
            if (string.IsNullOrWhiteSpace(root))
            {
                // local fallback
                root = Path.Combine(AppContext.BaseDirectory, "Storage");
            }
            return root;
        }

        private static string? ResolveProfileImageDiskPath(string storageRoot, string stored)
        {
            var s = (stored ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) return null;

            s = s.Replace('\\', '/');

            // URL form: /storage/ProfileImages/x.jpg
            if (s.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
            {
                var rel = s.Substring("/storage/".Length).TrimStart('/');
                // Force correct casing folder on disk
                if (rel.StartsWith("profileimages/", StringComparison.OrdinalIgnoreCase))
                    rel = "ProfileImages/" + rel.Substring("profileimages/".Length);

                return Path.Combine(storageRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            }

            // Disk-ish form: Storage/ProfileImages/x.jpg
            if (s.StartsWith("storage/", StringComparison.OrdinalIgnoreCase))
            {
                var rel = s.Substring("storage/".Length).TrimStart('/');
                if (rel.StartsWith("profileimages/", StringComparison.OrdinalIgnoreCase))
                    rel = "ProfileImages/" + rel.Substring("profileimages/".Length);

                return Path.Combine(storageRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            }

            // Filename-only fallback
            if (!s.Contains("/"))
                return Path.Combine(storageRoot, "ProfileImages", s);

            // fallback relative
            return Path.Combine(storageRoot, s.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        }

        private static string? NormalizeProfileImageUrl(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;

            var s = v.Trim().Replace('\\', '/');

            if (s.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
            {
                // enforce casing for folder in URL
                if (s.StartsWith("/storage/profileimages/", StringComparison.OrdinalIgnoreCase))
                    return "/storage/ProfileImages/" + s.Substring("/storage/profileimages/".Length);

                return s;
            }

            if (s.StartsWith("storage/", StringComparison.OrdinalIgnoreCase))
            {
                var rel = s.Substring("storage/".Length).TrimStart('/');
                if (rel.StartsWith("profileimages/", StringComparison.OrdinalIgnoreCase))
                    rel = "ProfileImages/" + rel.Substring("profileimages/".Length);

                return "/storage/" + rel;
            }

            // filename-only
            if (!s.Contains("/"))
                return "/storage/ProfileImages/" + s;

            return s.StartsWith("/") ? s : "/" + s;
        }
    }
}
