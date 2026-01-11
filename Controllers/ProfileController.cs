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
                user.IsEmailVerified = false; // force re-verification if email changes
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

        //Upload Profile Image
        [Authorize]
        [HttpPost("image")]
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

            if (user == null)
                return Unauthorized();

            var uploadsDir = Path.Combine(
                env.ContentRootPath,
                "Storage",
                "ProfileImages"
            );

            Directory.CreateDirectory(uploadsDir);

            // Delete old image
            if (!string.IsNullOrEmpty(user.ProfileImageUrl))
            {
                var oldPath = Path.Combine(
                    env.ContentRootPath,
                    user.ProfileImageUrl
                );
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }

            var ext = Path.GetExtension(file.FileName);
            var filename = $"user_{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}";
            var fullPath = Path.Combine(uploadsDir, filename);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            user.ProfileImageUrl = $"Storage/ProfileImages/{filename}";
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                imageUrl = $"{Request.Scheme}://{Request.Host}/storage/profileimages/{filename}"
            });
        }


        // Remove Profile Image
        [Authorize]
        [HttpDelete("image")]
        public async Task<IActionResult> RemoveProfileImage(
        [FromServices] IWebHostEnvironment env)
        {
            var userId = User.GetUserId();
            var user = await _db.Users.FindAsync(userId);

            if (user == null)
                return Unauthorized();

            if (!string.IsNullOrEmpty(user.ProfileImageUrl))
            {
                var fullPath = Path.Combine(env.ContentRootPath, user.ProfileImageUrl);
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);

                user.ProfileImageUrl = null;
                user.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return Ok();
        }



    }
}
