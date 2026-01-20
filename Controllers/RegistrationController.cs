using BCrypt.Net;
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Registration;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Institutions; // ✅ ADD
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/registration")]
    public class RegistrationController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly RegistrationService _registrationService;
        private readonly AuthService _authService;

        public RegistrationController(
            ApplicationDbContext db,
            RegistrationService registrationService,
            AuthService authService)
        {
            _db = db;
            _registrationService = registrationService;
            _authService = authService;
        }

        [HttpPost("intent")]
        public async Task<IActionResult> CreateRegistrationIntent([FromBody] CreateRegistrationIntentRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request.");

            // Normalize core fields early (prevents null assignment + whitespace duplicates)
            var email = (request.Email ?? "").Trim();
            var username = (request.Username ?? "").Trim();
            var password = request.Password ?? "";

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email is required.");

            if (string.IsNullOrWhiteSpace(username))
                return BadRequest("Username is required.");

            if (string.IsNullOrWhiteSpace(password))
                return BadRequest("Password is required.");

            // Use normalized email for uniqueness checks
            if (await _db.Users.AnyAsync(u => u.Email == email))
                return BadRequest("An account with this email already exists.");

            if (await _db.RegistrationIntents.AnyAsync(r => r.Email == email))
                return BadRequest("A registration is already in progress for this email.");
           // ✅ Institution path: ALWAYS validate BOTH domain + access code
            if (request.InstitutionId.HasValue)
            {
                var institution = await _db.Institutions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == request.InstitutionId.Value && i.IsActive);

                if (institution == null)
                    return BadRequest("Invalid or inactive institution.");

                // ✅ Validate email format + domain (ALWAYS) using normalized email
                var atIndex = email.LastIndexOf('@');
                if (atIndex <= 0 || atIndex == email.Length - 1)
                    return BadRequest("Invalid email address.");

                var emailDomain = email[(atIndex + 1)..].Trim().ToLowerInvariant();
                var instDomain = (institution.EmailDomain ?? "").Trim().ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(instDomain))
                    return BadRequest("Institution email domain is not configured. Please contact your administrator.");

                if (!string.Equals(emailDomain, instDomain, StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Email domain does not match institution.");

                // ✅ Reference number required for institution users
                if (string.IsNullOrWhiteSpace(request.ReferenceNumber))
                    return BadRequest("Reference number is required for institution users.");

                // ✅ Access code check (ALWAYS required for institution registrations)
                var expectedCode = (institution.InstitutionAccessCode ?? "").Trim();
                if (string.IsNullOrWhiteSpace(expectedCode))
                    return BadRequest("Institution access code is not configured. Please contact your administrator.");

                var providedCode = (request.InstitutionAccessCode ?? "").Trim();
                if (string.IsNullOrWhiteSpace(providedCode))
                    return BadRequest("Institution access code is required.");

                // Case-sensitive (matches your original). Change to OrdinalIgnoreCase if you want case-insensitive.
                if (!string.Equals(providedCode, expectedCode, StringComparison.Ordinal))
                    return BadRequest("Invalid institution access code.");
            }

            // Normalize optional fields to prevent "possible null assignment" warnings
            var firstName = (request.FirstName ?? "").Trim();
            var lastName = (request.LastName ?? "").Trim();
            var phoneNumber = (request.PhoneNumber ?? "").Trim();
            var referenceNumber = (request.ReferenceNumber ?? "").Trim();
            var institutionAccessCode = (request.InstitutionAccessCode ?? "").Trim();

            var intent = new RegistrationIntent
            {
                Email = email,
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),

                FirstName = firstName,
                LastName = lastName,
                PhoneNumber = phoneNumber,
                CountryId = request.CountryId,

                ReferenceNumber = referenceNumber,
                InstitutionAccessCode = institutionAccessCode,

                UserType = request.UserType,
                InstitutionId = request.InstitutionId,

                // ✅ persist chosen member type (defaults to Student)
                InstitutionMemberType = request.InstitutionMemberType
                    ?? LawAfrica.API.Models.Institutions.InstitutionMemberType.Student,

                ExpiresAt = DateTime.UtcNow.AddMinutes(60)
            };

            _db.RegistrationIntents.Add(intent);
            await _db.SaveChangesAsync();

            var nextAction = intent.UserType == UserType.Public
                ? "PAYMENT_REQUIRED"
                : "READY_FOR_ACCOUNT_CREATION";

            return Ok(new
            {
                registrationIntentId = intent.Id,
                nextAction
            });
        }

        [HttpPost("complete/{intentId}")]
        public async Task<IActionResult> CompleteRegistration(int intentId)
        {
            var intent = await _db.RegistrationIntents
                .FirstOrDefaultAsync(r => r.Id == intentId);

            if (intent == null)
                return NotFound("Registration intent not found.");

            try
            {
                var (user, twoFactor) = await _registrationService.CreateUserFromIntentAsync(intent);

                // (kept as-is; you already send inside service too; leaving unchanged to avoid "breaking")
                try
                {
                    await _authService.SendEmailVerificationAsync(user.Id);
                }
                catch
                {
                    // ignore email failures
                }

                return Ok(new
                {
                    message = "Account created successfully.",
                    requiresTwoFactorSetup = true,
                    requiresEmailVerification = true,
                });
            }
            catch (SeatLimitExceededException ex) // ✅ ADD: Seat limit => 409 Conflict
            {
                return Conflict(new
                {
                    message = ex.Message,
                    institutionId = ex.InstitutionId,
                    requestedType = ex.RequestedType.ToString(),
                    seatUsage = ex.Usage
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("intent/{intentId}/dev-mark-paid")]
        public async Task<IActionResult> DevMarkRegistrationPaid(int intentId)
        {
            var intent = await _db.RegistrationIntents.FirstOrDefaultAsync(x => x.Id == intentId);
            if (intent == null) return NotFound("Registration intent not found.");

            intent.PaymentCompleted = true;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Intent marked as paid (DEV ONLY).", intentId });
        }

        [AllowAnonymous]
        [HttpGet("intent/{intentId}/status")]
        public async Task<IActionResult> GetIntentStatus(int intentId)
        {
            var intent = await _db.RegistrationIntents
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == intentId);

            if (intent == null)
            {
                return Ok(new { status = "COMPLETED" });
            }

            return Ok(new
            {
                status = intent.PaymentCompleted ? "PAID" : "PENDING_PAYMENT",
                intentId = intent.Id,
                paymentCompleted = intent.PaymentCompleted,
                expiresAt = intent.ExpiresAt
            });
        }
    }
}
