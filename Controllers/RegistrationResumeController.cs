using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BCrypt.Net;
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Registration;
using LawAfrica.API.Models.Registration;
using LawAfrica.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/registration/resume")]
    public class RegistrationResumeController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        // ✅ CHANGE: we no longer send raw HTML via IEmailSender here.
        // We call AuthService which renders "registration-resume-otp" using your branded email templates.
        private readonly AuthService _authService;

        // Tune these as you want
        private const int OtpExpiryMinutes = 10;
        private const int OtpMaxAttempts = 5;
        private const int OtpResendCooldownSeconds = 60;
        private const int SessionExpiryMinutes = 30;

        // ✅ CHANGE: constructor now accepts AuthService instead of IEmailSender
        public RegistrationResumeController(ApplicationDbContext db, AuthService authService)
        {
            _db = db;
            _authService = authService;
        }

        private static string NormalizeEmail(string email)
            => (email ?? "").Trim().ToLowerInvariant();

        private static string Make6DigitCode()
            => RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");

        private static string Sha256Hex(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private string? GetClientIp()
            => HttpContext.Connection.RemoteIpAddress?.ToString();

        private string? GetUserAgent()
            => Request.Headers.UserAgent.ToString();

        // ✅ Helper: build a friendly display name for the email, if we have an intent
        private static string? DisplayNameFromIntent(RegistrationIntent? intent)
        {
            if (intent == null) return null;

            var full = $"{intent.FirstName} {intent.LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(full)) return full;

            if (!string.IsNullOrWhiteSpace(intent.FirstName)) return intent.FirstName;
            if (!string.IsNullOrWhiteSpace(intent.Username)) return intent.Username;

            return null;
        }

        // ---------------------------------------------------------
        // 1) Start OTP (Always 200 to avoid leaking account existence)
        // ---------------------------------------------------------
        [HttpPost("start-otp")]
        // ✅ CHANGE: accept CancellationToken so we can pass it into SaveChanges + AuthService email call
        public async Task<IActionResult> StartOtp([FromBody] StartResumeOtpRequest req, CancellationToken ct)
        {
            var email = NormalizeEmail(req?.Email ?? "");
            if (string.IsNullOrWhiteSpace(email))
                return Ok(new { ok = true, cooldownSeconds = OtpResendCooldownSeconds, expiresSeconds = OtpExpiryMinutes * 60 });

            var now = DateTime.UtcNow;

            // Reuse latest active challenge if it exists and not expired
            var existing = await _db.RegistrationResumeOtps
                .Where(x => x.EmailNormalized == email && !x.IsUsed && x.ExpiresAtUtc > now)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                var cooldownLeft = (int)Math.Max(
                    0,
                    (existing.LastSentAtUtc.AddSeconds(OtpResendCooldownSeconds) - now).TotalSeconds
                );

                if (cooldownLeft > 0)
                {
                    return Ok(new
                    {
                        ok = true,
                        cooldownSeconds = cooldownLeft,
                        expiresSeconds = (int)(existing.ExpiresAtUtc - now).TotalSeconds
                    });
                }

                // issue new code for existing record
                var code = Make6DigitCode();
                existing.CodeHash = BCrypt.Net.BCrypt.HashPassword(code);
                existing.Attempts = 0;
                existing.LastSentAtUtc = now;
                existing.IpAddress = GetClientIp();
                existing.UserAgent = GetUserAgent();

                await _db.SaveChangesAsync(ct);

                // ✅ CHANGE: Email sending is done here via AuthService (branded template)
                // Place: right after we persist the new OTP hash + timestamps.
                var pendingIntent = await FindPendingIntentForEmail(email, ct);
                var displayName = DisplayNameFromIntent(pendingIntent);

                await _authService.SendRegistrationResumeOtpEmailAsync(
                    toEmail: email,
                    otpCode: code,
                    otpExpiresAtUtc: existing.ExpiresAtUtc,
                    displayName: displayName,
                    ct: ct
                );

                return Ok(new
                {
                    ok = true,
                    cooldownSeconds = OtpResendCooldownSeconds,
                    expiresSeconds = (int)(existing.ExpiresAtUtc - now).TotalSeconds
                });
            }
            else
            {
                var code = Make6DigitCode();
                var challenge = new RegistrationResumeOtp
                {
                    EmailNormalized = email,
                    CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
                    ExpiresAtUtc = now.AddMinutes(OtpExpiryMinutes),
                    Attempts = 0,
                    IsUsed = false,
                    CreatedAtUtc = now,
                    LastSentAtUtc = now,
                    IpAddress = GetClientIp(),
                    UserAgent = GetUserAgent()
                };

                _db.RegistrationResumeOtps.Add(challenge);
                await _db.SaveChangesAsync(ct);

                // ✅ CHANGE: Email sending is done here via AuthService (branded template)
                // Place: right after we insert the OTP challenge, so the expiry/time is the persisted one.
                var pendingIntent = await FindPendingIntentForEmail(email, ct);
                var displayName = DisplayNameFromIntent(pendingIntent);

                await _authService.SendRegistrationResumeOtpEmailAsync(
                    toEmail: email,
                    otpCode: code,
                    otpExpiresAtUtc: challenge.ExpiresAtUtc,
                    displayName: displayName,
                    ct: ct
                );

                return Ok(new
                {
                    ok = true,
                    cooldownSeconds = OtpResendCooldownSeconds,
                    expiresSeconds = OtpExpiryMinutes * 60
                });
            }
        }

        // ---------------------------------------------------------
        // 2) Verify OTP -> issue resume token + return pending intent
        // ---------------------------------------------------------
        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyResumeOtpRequest req, CancellationToken ct)
        {
            var email = NormalizeEmail(req?.Email ?? "");
            var code = (req?.Code ?? "").Trim();

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
                return BadRequest("Email and code are required.");

            if (!Regex.IsMatch(code, @"^\d{6}$"))
                return BadRequest("Invalid code format.");

            var now = DateTime.UtcNow;

            var challenge = await _db.RegistrationResumeOtps
                .Where(x => x.EmailNormalized == email && !x.IsUsed)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (challenge == null || challenge.ExpiresAtUtc <= now)
                return BadRequest("Code expired. Please request a new code.");

            if (challenge.Attempts >= OtpMaxAttempts)
                return BadRequest("Too many attempts. Please request a new code.");

            challenge.Attempts += 1;

            var ok = BCrypt.Net.BCrypt.Verify(code, challenge.CodeHash);
            if (!ok)
            {
                await _db.SaveChangesAsync(ct);
                return BadRequest("Invalid code.");
            }

            challenge.IsUsed = true;
            await _db.SaveChangesAsync(ct);

            // Issue a short-lived resume token (DB-backed)
            var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var tokenHash = Sha256Hex(rawToken);

            _db.RegistrationResumeSessions.Add(new RegistrationResumeSession
            {
                EmailNormalized = email,
                TokenHash = tokenHash,
                ExpiresAtUtc = now.AddMinutes(SessionExpiryMinutes),
                CreatedAtUtc = now
            });
            await _db.SaveChangesAsync(ct);

            var pending = await FindPendingIntentForEmail(email, ct);

            var response = new VerifyResumeOtpResponse
            {
                ResumeToken = rawToken,
                Pending = pending == null
                    ? new PendingRegistrationResumeDto
                    {
                        HasPending = false
                    }
                    : new PendingRegistrationResumeDto
                    {
                        HasPending = true,
                        RegistrationIntentId = pending.Id,
                        Status = pending.PaymentCompleted ? "PAID" : "PENDING_PAYMENT",
                        ExpiresAt = pending.ExpiresAt,
                        NextAction = pending.UserType == UserType.Public
                            ? "PAYMENT_REQUIRED"
                            : "READY_FOR_ACCOUNT_CREATION"
                    }
            };

            return Ok(response);
        }

        // ---------------------------------------------------------
        // 3) Get pending by resume token
        // ---------------------------------------------------------
        [HttpGet("pending")]
        public async Task<IActionResult> Pending([FromHeader(Name = "X-Resume-Token")] string token, CancellationToken ct)
        {
            var email = await ValidateResumeTokenAndGetEmail(token, ct);
            if (email == null) return Unauthorized("Invalid/expired resume token.");

            var pending = await FindPendingIntentForEmail(email, ct);
            if (pending == null) return Ok(new { hasPending = false });

            return Ok(new
            {
                hasPending = true,
                registrationIntentId = pending.Id,
                status = pending.PaymentCompleted ? "PAID" : "PENDING_PAYMENT",
                expiresAt = pending.ExpiresAt,
                nextAction = pending.UserType == UserType.Public ? "PAYMENT_REQUIRED" : "READY_FOR_ACCOUNT_CREATION"
            });
        }

        // ---------------------------------------------------------
        // 4) Resend Mpesa prompt (token-based)
        //    You can internally call your Mpesa initiation logic/service.
        // ---------------------------------------------------------
        [HttpPost("mpesa/resend")]
        public async Task<IActionResult> ResendMpesa(
            [FromHeader(Name = "X-Resume-Token")] string token,
            [FromBody] ResendMpesaPromptRequest req,
            CancellationToken ct)
        {
            var email = await ValidateResumeTokenAndGetEmail(token, ct);
            if (email == null) return Unauthorized("Invalid/expired resume token.");

            var phone = (req?.PhoneNumber ?? "").Trim();
            if (string.IsNullOrWhiteSpace(phone))
                return BadRequest("PhoneNumber is required.");

            var intent = await FindPendingIntentForEmail(email, ct);
            if (intent == null) return NotFound("No pending registration found for this email.");

            if (intent.UserType != UserType.Public)
                return BadRequest("Mpesa resend is only for public registrations.");

            // If intent is old/expired, extend it when resuming:
            if (intent.ExpiresAt < DateTime.UtcNow)
            {
                intent.ExpiresAt = DateTime.UtcNow.AddMinutes(60);
                _db.RegistrationIntents.Update(intent);
                await _db.SaveChangesAsync(ct);
            }

            // ✅ Call your existing Mpesa endpoint logic.
            // Best practice: extract your STK initiate logic into a service and call it here.
            // For now, we just return the intent id so your frontend can call:
            // POST /payments/mpesa/stk/initiate with registrationIntentId + phone.
            return Ok(new
            {
                registrationIntentId = intent.Id,
                message = "Resume verified. You can re-initiate Mpesa prompt using this intent id."
            });
        }

        // ----------------- Helpers -----------------

        private async Task<string?> ValidateResumeTokenAndGetEmail(string rawToken, CancellationToken ct)
        {
            rawToken = (rawToken ?? "").Trim();
            if (string.IsNullOrWhiteSpace(rawToken)) return null;

            var now = DateTime.UtcNow;
            var tokenHash = Sha256Hex(rawToken);

            var session = await _db.RegistrationResumeSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.TokenHash == tokenHash &&
                    x.RevokedAtUtc == null &&
                    x.ExpiresAtUtc > now, ct);

            return session?.EmailNormalized;
        }

        private async Task<RegistrationIntent?> FindPendingIntentForEmail(string emailNormalized, CancellationToken ct)
        {
            // Note: you currently never delete intents here; missing intent is treated as completed in status endpoint.
            // We'll return most recent that isn't already "completed by deletion".
            var intent = await _db.RegistrationIntents
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(x => x.Email == emailNormalized, ct);

            // If you later add a "CompletedAt" on intent, filter it here.
            return intent;
        }
    }
}
