using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/bootstrap")]
    public class BootstrapController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _config;

        public BootstrapController(ApplicationDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public class BootstrapAdminRequest
        {
            public string Username { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string PhoneNumber { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
        }

        [HttpPost("admin")]
        public async Task<IActionResult> CreateAdmin([FromBody] BootstrapAdminRequest request)
        {
            // 1) Must provide bootstrap token (from Render env var)
            var expected = _config["BOOTSTRAP_TOKEN"];
            var provided = Request.Headers["X-Bootstrap-Token"].ToString();

            if (string.IsNullOrWhiteSpace(expected))
                return StatusCode(500, "BOOTSTRAP_TOKEN is not set on the server.");

            if (string.IsNullOrWhiteSpace(provided) || provided != expected)
                return Unauthorized("Missing/invalid bootstrap token.");

            // 2) Only allow if NO global admin exists yet
            var adminExists = await _db.Users.AnyAsync(u => u.IsGlobalAdmin);
            if (adminExists)
                return BadRequest("A global admin already exists. Bootstrap is disabled.");

            // 3) Basic validation
            request.Username = (request.Username ?? "").Trim();
            request.Email = (request.Email ?? "").Trim().ToLowerInvariant();
            request.PhoneNumber = (request.PhoneNumber ?? "").Trim();

            if (string.IsNullOrWhiteSpace(request.Username)) return BadRequest("Username is required.");
            if (string.IsNullOrWhiteSpace(request.Email)) return BadRequest("Email is required.");
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                return BadRequest("Password must be at least 8 characters.");

            // 4) Prevent duplicates
            var emailExists = await _db.Users.AnyAsync(u => u.Email.ToLower() == request.Email);
            if (emailExists) return BadRequest("Email already exists.");

            var usernameExists = await _db.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower());
            if (usernameExists) return BadRequest("Username already exists.");

            // 5) Hash password
            var hash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var admin = new User
            {
                Username = request.Username,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                PasswordHash = hash,

                FirstName = request.FirstName,
                LastName = request.LastName,

                UserType = UserType.Admin,
                Role = "Admin",
                IsGlobalAdmin = true,

                IsActive = true,
                IsApproved = true,
                IsEmailVerified = true,

                TwoFactorEnabled = false,
                FailedLoginAttempts = 0,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(admin);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Global admin created successfully.",
                admin.Id,
                admin.Username,
                admin.Email
            });
        }
    }
}
