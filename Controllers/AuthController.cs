// Controllers/AuthController.cs
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs;
using LawAfrica.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _configuration;

        // ✅ Product rule: we do NOT send email verification emails anymore.
        // Email becomes verified after successful 2FA setup verification.
        private const bool EMAIL_VERIFICATION_DISABLED = true;

        public AuthController(AuthService authService, ApplicationDbContext db, IConfiguration configuration)
        {
            _authService = authService;
            _db = db;
            _configuration = configuration;
        }

        // ---------------------------------------------------------
        // Audit helper (never breaks login flow if audit fails)
        // ---------------------------------------------------------
        private async Task WriteLoginAuditAsync(int? userId, string username, bool success, string reason)
        {
            try
            {
                var ip = HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
                var ua = Request?.Headers["User-Agent"].ToString() ?? "Unknown";

                _db.LoginAudits.Add(new LoginAudit
                {
                    UserId = userId,
                    UserName = username ?? string.Empty,
                    IpAddress = ip,
                    UserAgent = ua,
                    LoggedInAt = DateTime.UtcNow,
                    IsSuccessful = success,
                    FailureReason = success ? "" : (reason ?? "")
                });

                await _db.SaveChangesAsync();
            }
            catch
            {
                // Never let audit failures block auth.
            }
        }

        // =========================================================
        // LOGIN (Step 1)
        // =========================================================
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request.");

            var identifier = (request.Username ?? "").Trim(); // username OR email
            var password = request.Password ?? "";

            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(password))
                return BadRequest("Username or email and password are required.");

            var identLower = identifier.ToLowerInvariant();

            // ✅ Find by username OR email for consistent lockout + audit id
            var userRow = await _db.Users
                .AsNoTracking()
                .Where(u =>
                    u.Username.ToLower() == identLower ||
                    (u.Email != null && u.Email.ToLower() == identLower)
                )
                .Select(u => new { u.Id, u.LockoutEndAt })
                .FirstOrDefaultAsync();

            // ✅ Early lockout response
            if (userRow?.LockoutEndAt.HasValue == true && userRow.LockoutEndAt.Value > DateTime.UtcNow)
            {
                await WriteLoginAuditAsync(userRow.Id, identifier, success: false, reason: "Account locked.");
                return BadRequest($"Account locked until {userRow.LockoutEndAt.Value:u}");
            }

            var result = await _authService.LoginAsync(request);

            if (!result.Success)
            {
                await WriteLoginAuditAsync(userRow?.Id, identifier, success: false, reason: result.Message ?? "Login failed.");
                return BadRequest(result.Message ?? "Incorrect username/email or password. Please try again.");
            }

            if (result.Requires2FASetup)
            {
                return Ok(new
                {
                    requires2FASetup = true,
                    requires2FA = false,
                    userId = result.UserId
                });
            }

            if (result.Requires2FA)
            {
                return Ok(new
                {
                    requires2FASetup = false,
                    requires2FA = true,
                    userId = result.UserId
                });
            }

            if (!string.IsNullOrWhiteSpace(result.Token))
            {
                await WriteLoginAuditAsync(result.UserId, identifier, success: true, reason: "");
                return Ok(new { token = result.Token });
            }

            // fallback (shouldn't usually happen)
            return Ok(new
            {
                requires2FASetup = false,
                requires2FA = true,
                userId = result.UserId
            });
        }

        // =========================================================
        // LOGIN (Step 2: confirm 2FA and issue JWT)
        // =========================================================
        public class ConfirmTwoFactorRequest
        {
            // Frontend already sends "Username" (can be username OR email).
            public string Username { get; set; } = string.Empty;
            public string Code { get; set; } = string.Empty;
        }

        [AllowAnonymous]
        [HttpPost("confirm-2fa")]
        public async Task<IActionResult> ConfirmTwoFactor([FromBody] ConfirmTwoFactorRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request.");

            var ident = (request.Username ?? "").Trim(); // can be username OR email
            var code = (request.Code ?? "").Trim();

            if (string.IsNullOrWhiteSpace(ident) || string.IsNullOrWhiteSpace(code))
                return BadRequest("Username or email and code are required.");

            var identUpper = ident.ToUpperInvariant();
            var identLower = ident.ToLowerInvariant();

            // Lookup for lockout/audit
            var userRow = await _db.Users
                .AsNoTracking()
                .Where(u =>
                    (!string.IsNullOrWhiteSpace(u.NormalizedUsername) && u.NormalizedUsername == identUpper) ||
                    (!string.IsNullOrWhiteSpace(u.Email) && u.Email.Trim().ToLower() == identLower)
                )
                .Select(u => new { u.Id, u.LockoutEndAt })
                .FirstOrDefaultAsync();

            if (userRow?.LockoutEndAt.HasValue == true && userRow.LockoutEndAt.Value > DateTime.UtcNow)
            {
                await WriteLoginAuditAsync(userRow.Id, ident, success: false, reason: "Account locked.");
                return BadRequest($"Account locked until {userRow.LockoutEndAt.Value:u}");
            }

            var (ok, token, error) = await _authService.VerifyTwoFactorLoginAsync(ident, code);

            if (!ok || string.IsNullOrWhiteSpace(token))
            {
                var msg = error ?? "The verification code you entered is incorrect or expired. Please try again.";
                await WriteLoginAuditAsync(userRow?.Id, ident, success: false, reason: msg);
                return BadRequest(msg);
            }

            await WriteLoginAuditAsync(userRow?.Id, ident, success: true, reason: "");
            return Ok(new { token });
        }

        // =========================================================
        // 2FA setup link redirect (used in 2FA emails)
        // GET: /api/auth/twofactor-setup?token=xxxx
        // =========================================================
        [AllowAnonymous]
        [HttpGet("twofactor-setup")]
        public IActionResult TwoFactorSetupLink([FromQuery] string token)
        {
            token = (token ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest("Token is required.");

            // Prefer explicit URL:
            var frontend2FaUrl = (_configuration["FrontendTwoFactorSetupUrl"] ?? "").Trim();

            // Fallback to FrontendUrl / FrontendBaseUrl
            if (string.IsNullOrWhiteSpace(frontend2FaUrl))
            {
                var frontendUrl = (_configuration["FrontendUrl"] ?? "").Trim().TrimEnd('/');
                var frontendBaseUrl = (_configuration["FrontendBaseUrl"] ?? "").Trim().TrimEnd('/');
                var baseUrl = !string.IsNullOrWhiteSpace(frontendUrl) ? frontendUrl : frontendBaseUrl;

                if (!string.IsNullOrWhiteSpace(baseUrl))
                    frontend2FaUrl = $"{baseUrl}/twofactor-setup";
            }

            // If misconfigured, return token in plain text so user can copy
            if (string.IsNullOrWhiteSpace(frontend2FaUrl))
                return Content($"Setup token: {System.Net.WebUtility.HtmlEncode(token)}", "text/plain");

            var separator = frontend2FaUrl.Contains("?") ? "&" : "?";
            var url = $"{frontend2FaUrl}{separator}token={Uri.EscapeDataString(token)}";

            return Redirect(url);
        }

        // =========================================================
        // EMAIL VERIFICATION (DISABLED)
        // - Keep endpoint for backward compatibility (old links)
        // - Redirects to login with a clear signal that it's disabled
        // =========================================================
        [AllowAnonymous]
        [HttpGet("verify-email")]
        public IActionResult VerifyEmail([FromQuery] string token)
        {
            // Token ignored; verification is done via 2FA setup.
            var frontendVerifiedUrl = (_configuration["FrontendEmailVerifiedUrl"] ?? "").Trim().TrimEnd('/');
            var frontendUrl = (_configuration["FrontendUrl"] ?? "").Trim().TrimEnd('/');
            var frontendBaseUrl = (_configuration["FrontendBaseUrl"] ?? "").Trim().TrimEnd('/');

            if (string.IsNullOrWhiteSpace(frontendVerifiedUrl))
            {
                var baseUrl = !string.IsNullOrWhiteSpace(frontendUrl) ? frontendUrl : frontendBaseUrl;
                if (!string.IsNullOrWhiteSpace(baseUrl))
                    frontendVerifiedUrl = $"{baseUrl}/login";
            }

            if (!string.IsNullOrWhiteSpace(frontendVerifiedUrl))
            {
                // verified=0 + reason helps frontend show correct message
                return Redirect($"{frontendVerifiedUrl}?verified=0&reason=disabled");
            }

            // Fallback plain response
            return BadRequest("Email verification is disabled. Please complete 2FA setup from your email.");
        }

        // =========================================================
        // SEND VERIFICATION EMAIL (DISABLED)
        // =========================================================
        public class SendVerificationRequest
        {
            public string EmailOrUsername { get; set; } = string.Empty;
        }

        [AllowAnonymous]
        [HttpPost("send-verification")]
        public IActionResult SendVerification([FromBody] SendVerificationRequest req)
        {
            // We do not send verification emails anymore.
            return Ok(new
            {
                message = "Email verification is not used. Please check your inbox for the 2FA setup email and complete setup to activate your account."
            });
        }

        // =========================================================
        // RESEND EMAIL VERIFICATION (DISABLED)
        // =========================================================
        public class ResendVerificationRequest
        {
            public string EmailOrUsername { get; set; } = string.Empty;
        }

        [AllowAnonymous]
        [HttpPost("resend-verification")]
        public IActionResult ResendVerification([FromBody] ResendVerificationRequest req)
        {
            return Ok(new
            {
                message = "Email verification is not used. Please check your inbox for the 2FA setup email and complete setup to activate your account."
            });
        }

        // =========================================================
        // PASSWORD RESET (request link)
        // =========================================================
        public class PasswordResetRequest
        {
            public string Email { get; set; } = string.Empty;
        }

        [AllowAnonymous]
        [HttpPost("request-password-reset")]
        public async Task<IActionResult> RequestPasswordReset([FromBody] PasswordResetRequest req)
        {
            var email = (req?.Email ?? "").Trim();
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email is required.");

            try { await _authService.RequestPasswordResetAsync(email); } catch { }

            return Ok(new { message = "If the account exists, a password reset link has been sent." });
        }

        // =========================================================
        // PASSWORD RESET (apply new password)
        // =========================================================
        public class ApplyPasswordResetRequest
        {
            public string Token { get; set; } = string.Empty;
            public string NewPassword { get; set; } = string.Empty;
        }

        [AllowAnonymous]
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ApplyPasswordResetRequest req)
        {
            var token = (req?.Token ?? "").Trim();
            var newPassword = req?.NewPassword ?? "";

            if (string.IsNullOrWhiteSpace(token))
                return BadRequest("Token is required.");

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
                return BadRequest("Password must be at least 8 characters.");

            var ok = await _authService.ResetPasswordAsync(token, newPassword);
            if (!ok)
                return BadRequest("Invalid or expired reset token.");

            return Ok(new { message = "Password reset successful." });
        }

        //Mobile only password reste:

        // =========================================================
        // PASSWORD RESET (mobile: via TOTP, no email link)
        // POST: /api/auth/reset-password-totp
        // =========================================================
        public class ResetPasswordTotpRequest
        {
            public string Username { get; set; } = string.Empty; // username OR email
            public string Code { get; set; } = string.Empty;     // 6-digit TOTP
            public string NewPassword { get; set; } = string.Empty;
        }

        [AllowAnonymous]
        [HttpPost("reset-password-totp")]
        public async Task<IActionResult> ResetPasswordWithTotp([FromBody] ResetPasswordTotpRequest req)
        {
            var ident = (req?.Username ?? "").Trim();
            var code = (req?.Code ?? "").Trim();
            var newPassword = req?.NewPassword ?? "";

            if (string.IsNullOrWhiteSpace(ident) || string.IsNullOrWhiteSpace(code))
                return BadRequest("Username/email and code are required.");

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
                return BadRequest("Password must be at least 8 characters.");

            var ip = HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
            var ua = Request?.Headers["User-Agent"].ToString() ?? "Unknown";

            var (ok, error) = await _authService.ResetPasswordWithTotpAsync(ident, code, newPassword, ip, ua);

            if (!ok)
            {
                // ✅ Generic message (avoid user enumeration)
                return BadRequest(error ?? "Unable to reset password.");
            }

            // ✅ No reset link email. Only alert email is sent by the service.
            return Ok(new { message = "Password reset successful." });
        }


        // =========================================================
        // PASSWORD RESET (clicked from email link)
        // GET: /api/auth/reset-password?token=xxxx
        // =========================================================
        [AllowAnonymous]
        [HttpGet("reset-password")]
        public IActionResult ResetPasswordLink([FromQuery] string token)
        {
            token = (token ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest("Token is required.");

            // Preferred: redirect to frontend reset page
            var frontendResetUrl = (_configuration["FrontendResetPasswordUrl"] ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(frontendResetUrl))
            {
                var separator = frontendResetUrl.Contains("?") ? "&" : "?";
                var url = $"{frontendResetUrl}{separator}token={Uri.EscapeDataString(token)}";
                return Redirect(url);
            }

            // Fallback HTML (kept from your existing behavior)
            var appUrl = (_configuration["AppUrl"] ?? "").Trim().TrimEnd('/');
            var logoUrl = string.IsNullOrWhiteSpace(appUrl) ? "" : $"{appUrl}/logo.png";

            var safeToken = System.Net.WebUtility.HtmlEncode(token);

            var logoBlock = string.IsNullOrWhiteSpace(logoUrl)
                ? @"<div style=""font-size:22px;font-weight:800;letter-spacing:0.2px;color:#101828;"">Law<span style=""color:#801010;"">Africa</span></div>"
                : $@"<img src=""{System.Net.WebUtility.HtmlEncode(logoUrl)}"" alt=""LawAfrica"" width=""160"" style=""display:block;border:0;outline:none;text-decoration:none;height:auto;"" />";

            var html = $@"
            <!DOCTYPE html>
            <html lang=""en"">
            <head>
              <meta charset=""utf-8"" />
              <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
              <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"" />
              <title>LawAfrica | Password Reset</title>
            </head>
            <body style=""margin:0;padding:0;background:#F6F7FB;-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%;font-family:Arial,Helvetica,sans-serif;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#F6F7FB;border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;"">
                <tr>
                  <td align=""center"" style=""padding:24px 12px;"">
                    <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""680"" style=""width:680px;max-width:680px;background:#ffffff;border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;border-radius:16px;overflow:hidden;box-shadow:0 10px 30px rgba(16,24,40,0.08);"">
                      <tr><td style=""background:#801010;height:6px;line-height:6px;font-size:0;"">&nbsp;</td></tr>
                      <tr>
                        <td style=""padding:22px 24px 10px 24px;"">
                          <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"">
                            <tr>
                              <td align=""left"" valign=""middle"">
                                {logoBlock}
                                <div style=""margin-top:6px;font-size:13px;line-height:18px;color:#667085;"">Know. Do. Be More</div>
                              </td>
                              <td align=""right"" valign=""middle"" style=""font-size:12px;color:#98A2B3;"">Password reset</td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style=""padding:8px 24px 24px 24px;"">
                          <div style=""font-size:22px;line-height:28px;font-weight:800;color:#101828;margin:0 0 12px 0;"">Reset your password</div>
                          <div style=""font-size:14px;line-height:22px;color:#344054;margin:0 0 16px 0;"">
                            You opened a password reset link. If your app did not automatically open,
                            copy the token below and paste it into the password reset form in the LawAfrica app.
                          </div>

                          <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""margin:0 0 14px 0;"">
                            <tr>
                              <td style=""background:#F9FAFB;border:1px solid #EAECF0;border-radius:12px;padding:14px;"">
                                <div style=""font-size:12px;line-height:18px;color:#667085;margin:0 0 8px 0;font-weight:700;"">Reset token</div>
                                <div style=""font-family:Consolas,Menlo,Monaco,monospace;font-size:12px;line-height:18px;color:#101828;word-break:break-all;white-space:pre-wrap;"">{safeToken}</div>
                              </td>
                            </tr>
                          </table>

                          <div style=""font-size:13px;line-height:20px;color:#667085;margin:0 0 18px 0;"">
                            If you did not request a password reset, you can safely close this page.
                          </div>

                          <div style=""height:1px;background:#EAECF0;line-height:1px;font-size:0;margin:18px 0;"">&nbsp;</div>

                          <div style=""font-size:13px;line-height:20px;color:#667085;margin:0;"">
                            Need help? Contact <span style=""color:#1D4ED8;font-weight:700;"">support@lawafrica.com</span>.
                          </div>
                        </td>
                      </tr>

                      <tr>
                        <td style=""padding:16px 24px 22px 24px;background:#ffffff;"">
                          <div style=""font-size:12px;line-height:18px;color:#98A2B3;"">&copy; {DateTime.UtcNow.Year} LawAfrica. All rights reserved.</div>
                          <div style=""font-size:12px;line-height:18px;color:#98A2B3;margin-top:6px;"">Please do not reply to this email.</div>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>";

            return Content(html, "text/html");
        }
    }
}
