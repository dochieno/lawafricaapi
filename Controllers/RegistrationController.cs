using BCrypt.Net;
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Registration;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Institutions;
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
        private readonly ILogger<RegistrationController> _logger;

        public RegistrationController(
            ApplicationDbContext db,
            RegistrationService registrationService,
            AuthService authService,
            ILogger<RegistrationController> logger)
        {
            _db = db;
            _registrationService = registrationService;
            _authService = authService;
            _logger = logger;
        }

        // =========================================================
        // NEW: Resolve institution from email domain and/or access code
        // POST: /api/registration/resolve-institution
        // Body: { email?: string, institutionAccessCode?: string }
        // Always returns 200 with ok=true/false (no leakage of details beyond "not found")
        // =========================================================
        public class ResolveInstitutionRequest
        {
            public string? Email { get; set; }
            public string? InstitutionAccessCode { get; set; }
        }

        [AllowAnonymous]
        [HttpPost("resolve-institution")]
        public async Task<IActionResult> ResolveInstitution([FromBody] ResolveInstitutionRequest req, CancellationToken ct)
        {
            var email = (req?.Email ?? "").Trim();
            var code = NormalizeAccessCode(req?.InstitutionAccessCode);

            // Prefer access code because it's globally unique
            if (!string.IsNullOrWhiteSpace(code))
            {
                var inst = await _db.Institutions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(i =>
                        i.InstitutionAccessCode != null &&
                        i.InstitutionAccessCode.Trim().ToUpper() == code, ct);

                if (inst == null)
                    return Ok(new { ok = false });

                return Ok(new
                {
                    ok = true,
                    institutionId = inst.Id,
                    name = inst.Name,
                    emailDomain = inst.EmailDomain,
                    isActive = inst.IsActive
                });
            }

            // Fallback: resolve via email domain
            var domain = ExtractEmailDomain(email);
            if (string.IsNullOrWhiteSpace(domain))
                return Ok(new { ok = false });

            var instByDomain = await _db.Institutions
                .AsNoTracking()
                .FirstOrDefaultAsync(i =>
                    i.EmailDomain != null &&
                    i.EmailDomain.Trim().ToLower() == domain.ToLower(), ct);

            if (instByDomain == null)
                return Ok(new { ok = false });

            return Ok(new
            {
                ok = true,
                institutionId = instByDomain.Id,
                name = instByDomain.Name,
                emailDomain = instByDomain.EmailDomain,
                isActive = instByDomain.IsActive
            });
        }

        // =========================================================
        // Registration Intent
        // =========================================================
        [HttpPost("intent")]
        public async Task<IActionResult> CreateRegistrationIntent([FromBody] CreateRegistrationIntentRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest(new
                    {
                        message = "Invalid request.",
                        traceId = HttpContext.TraceIdentifier
                    });

                // ============================
                // Normalize core fields
                // ============================
                var email = (request.Email ?? "").Trim();
                var username = (request.Username ?? "").Trim();
                var password = request.Password ?? "";

                // ✅ Reference number: keep nullable (empty => null)
                var referenceNumber = string.IsNullOrWhiteSpace(request.ReferenceNumber)
                    ? null
                    : request.ReferenceNumber.Trim();

                if (string.IsNullOrWhiteSpace(email))
                    return BadRequest(new { message = "Email is required.", traceId = HttpContext.TraceIdentifier });

                if (string.IsNullOrWhiteSpace(username))
                    return BadRequest(new { message = "Username is required.", traceId = HttpContext.TraceIdentifier });

                if (string.IsNullOrWhiteSpace(password))
                    return BadRequest(new { message = "Password is required.", traceId = HttpContext.TraceIdentifier });

                // ============================
                // Uniqueness checks
                // ============================
                if (await _db.Users.AnyAsync(u => u.Email == email))
                    return BadRequest(new
                    {
                        message = "An account with this email already exists.",
                        traceId = HttpContext.TraceIdentifier
                    });

                var existing = await _db.RegistrationIntents
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefaultAsync(r => r.Email == email);

                if (existing != null)
                {
                    var next = existing.UserType == UserType.Public
                        ? "PAYMENT_REQUIRED"
                        : "READY_FOR_ACCOUNT_CREATION";

                    return Ok(new
                    {
                        registrationIntentId = existing.Id,
                        nextAction = next
                    });
                }

                // ============================
                // Normalize optional fields
                // ============================
                var firstName = (request.FirstName ?? "").Trim();
                var lastName = (request.LastName ?? "").Trim();
                var phoneNumber = (request.PhoneNumber ?? "").Trim();

                // Access code normalize (uppercase, trimmed)
                var institutionAccessCode = NormalizeAccessCode(request.InstitutionAccessCode);

                // ============================
                // Institution inference + validation path
                // ============================
                Institution? institution = null;

                var isPublic = request.UserType == UserType.Public;

                if (!isPublic)
                {
                    // ✅ If InstitutionId not provided, infer it using globally-unique access code
                    if (!request.InstitutionId.HasValue)
                    {
                        if (string.IsNullOrWhiteSpace(institutionAccessCode))
                            return BadRequest(new
                            {
                                message = "Institution access code is required.",
                                traceId = HttpContext.TraceIdentifier
                            });

                        institution = await _db.Institutions
                            .AsNoTracking()
                            .FirstOrDefaultAsync(i =>
                                i.InstitutionAccessCode != null &&
                                i.InstitutionAccessCode.Trim().ToUpper() == institutionAccessCode, default);

                        if (institution == null)
                            return BadRequest(new
                            {
                                message = "Invalid institution access code.",
                                traceId = HttpContext.TraceIdentifier
                            });

                        if (!institution.IsActive)
                            return BadRequest(new
                            {
                                message = "Invalid or inactive institution.",
                                traceId = HttpContext.TraceIdentifier
                            });

                        // ✅ Assign inferred InstitutionId (so intent saves InstitutionId as required by your flow)
                        request.InstitutionId = institution.Id;
                    }
                    else
                    {
                        // If InstitutionId provided, load that institution
                        institution = await _db.Institutions
                            .AsNoTracking()
                            .FirstOrDefaultAsync(i => i.Id == request.InstitutionId.Value && i.IsActive);

                        if (institution == null)
                            return BadRequest(new
                            {
                                message = "Invalid or inactive institution.",
                                traceId = HttpContext.TraceIdentifier
                            });
                    }

                    // Validate email format + domain match
                    var emailDomain = ExtractEmailDomain(email);
                    if (string.IsNullOrWhiteSpace(emailDomain))
                        return BadRequest(new
                        {
                            message = "Invalid email address.",
                            traceId = HttpContext.TraceIdentifier
                        });

                    var instDomain = (institution.EmailDomain ?? "").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(instDomain))
                        return BadRequest(new
                        {
                            message = "Institution email domain is not configured. Please contact your administrator.",
                            traceId = HttpContext.TraceIdentifier
                        });

                    if (!string.Equals(emailDomain, instDomain, StringComparison.OrdinalIgnoreCase))
                        return BadRequest(new
                        {
                            message = "Email domain does not match institution.",
                            traceId = HttpContext.TraceIdentifier
                        });

                    // Access code validation (must match institution)
                    var expectedCode = NormalizeAccessCode(institution.InstitutionAccessCode);
                    if (string.IsNullOrWhiteSpace(expectedCode))
                        return BadRequest(new
                        {
                            message = "Institution access code is not configured. Please contact your administrator.",
                            traceId = HttpContext.TraceIdentifier
                        });

                    if (string.IsNullOrWhiteSpace(institutionAccessCode))
                        return BadRequest(new
                        {
                            message = "Institution access code is required.",
                            traceId = HttpContext.TraceIdentifier
                        });

                    if (!string.Equals(institutionAccessCode, expectedCode, StringComparison.Ordinal))
                        return BadRequest(new
                        {
                            message = "Invalid institution access code.",
                            traceId = HttpContext.TraceIdentifier
                        });

                    // ✅ Reference number required for institution users
                    if (string.IsNullOrWhiteSpace(referenceNumber))
                        return BadRequest(new
                        {
                            message = "Reference number is required for institution users.",
                            traceId = HttpContext.TraceIdentifier
                        });

                    if (referenceNumber.Length < 3)
                        return BadRequest(new
                        {
                            message = "Reference number looks too short.",
                            traceId = HttpContext.TraceIdentifier
                        });
                }
                else
                {
                    // ✅ Public signup: ensure we never store empty-string reference numbers
                    referenceNumber = null;

                    // Public users should not carry institution metadata
                    request.InstitutionId = null;
                    request.InstitutionMemberType = null;
                    institutionAccessCode = "";
                }

                var intent = new RegistrationIntent
                {
                    Email = email,
                    Username = username,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),

                    FirstName = firstName,
                    LastName = lastName,
                    PhoneNumber = phoneNumber,
                    CountryId = request.CountryId,

                    InstitutionAccessCode = institutionAccessCode,

                    ReferenceNumber = referenceNumber,

                    UserType = request.UserType,

                    // ✅ Will be inferred above for institution flows (so it is present)
                    InstitutionId = request.InstitutionId,

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
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "CreateRegistrationIntent failed. TraceId={TraceId}",
                    HttpContext.TraceIdentifier
                );

                return StatusCode(500, new
                {
                    message = "Internal server error while creating registration intent.",
                    traceId = HttpContext.TraceIdentifier
                });
            }
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
            catch (SeatLimitExceededException ex)
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

        // ---------------- Helpers ----------------

        private static string NormalizeAccessCode(string? code)
            => string.IsNullOrWhiteSpace(code) ? "" : code.Trim().ToUpperInvariant();

        private static string ExtractEmailDomain(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "";
            var at = email.LastIndexOf('@');
            if (at <= 0 || at >= email.Length - 1) return "";
            return email[(at + 1)..].Trim().ToLowerInvariant();
        }
    }
}