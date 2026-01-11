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

            var username = (request.Username ?? "").Trim();
            var password = request.Password ?? "";

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return BadRequest("Username and password are required.");

            var userRow = await _db.Users
                .AsNoTracking()
                .Where(u => u.Username.ToLower() == username.ToLower())
                .Select(u => new { u.Id, u.LockoutEndAt })
                .FirstOrDefaultAsync();

            var result = await _authService.LoginAsync(request);

            if (!result.Success)
            {
                await WriteLoginAuditAsync(userRow?.Id, username, success: false, reason: result.Message ?? "Login failed.");

                if (userRow?.LockoutEndAt.HasValue == true && userRow.LockoutEndAt.Value > DateTime.UtcNow)
                    return BadRequest($"Account locked until {userRow.LockoutEndAt.Value:u}");

                return BadRequest(result.Message ?? "Incorrect username or password. Please try again.");
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
                await WriteLoginAuditAsync(result.UserId, username, success: true, reason: "");
                return Ok(new { token = result.Token });
            }

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
            public string Username { get; set; } = string.Empty;
            public string Code { get; set; } = string.Empty;
        }

        [AllowAnonymous]
        [HttpPost("confirm-2fa")]
        public async Task<IActionResult> ConfirmTwoFactor([FromBody] ConfirmTwoFactorRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request.");

            var username = (request.Username ?? "").Trim();
            var code = (request.Code ?? "").Trim();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code))
                return BadRequest("Username and code are required.");

            var userRow = await _db.Users
                .AsNoTracking()
                .Where(u => u.Username.ToLower() == username.ToLower())
                .Select(u => new { u.Id, u.LockoutEndAt })
                .FirstOrDefaultAsync();

            if (userRow?.LockoutEndAt.HasValue == true && userRow.LockoutEndAt.Value > DateTime.UtcNow)
            {
                await WriteLoginAuditAsync(userRow.Id, username, success: false, reason: "Account locked.");
                return BadRequest($"Account locked until {userRow.LockoutEndAt.Value:u}");
            }

            var (ok, token, error) = await _authService.VerifyTwoFactorLoginAsync(username, code);

            if (!ok || string.IsNullOrWhiteSpace(token))
            {
                var msg = error ?? "The verification code you entered is incorrect or expired. Please try again.";
                await WriteLoginAuditAsync(userRow?.Id, username, success: false, reason: msg);
                return BadRequest(msg);
            }

            await WriteLoginAuditAsync(userRow?.Id, username, success: true, reason: "");
            return Ok(new { token });
        }

        // =========================================================
        // EMAIL VERIFICATION
        // =========================================================
        [AllowAnonymous]
        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string token)
        {
            token = (token ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest("Token is required.");

            var config = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;

            // ✅ Your frontend landing page after verification
            // Recommended appsettings key:
            // "FrontendEmailVerifiedUrl": "http://localhost:5173/login"
            // or "https://app.lawafrica.com/login"
            var frontendVerifiedUrl = (config?["FrontendEmailVerifiedUrl"] ?? "").Trim().TrimEnd('/');

            // Optional: fallback base URL if you prefer:
            // "FrontendBaseUrl": "http://localhost:5173"
            var frontendBaseUrl = (config?["FrontendBaseUrl"] ?? "").Trim().TrimEnd('/');

            // If FrontendEmailVerifiedUrl not set but base url is, default to /login
            if (string.IsNullOrWhiteSpace(frontendVerifiedUrl) && !string.IsNullOrWhiteSpace(frontendBaseUrl))
                frontendVerifiedUrl = $"{frontendBaseUrl}/login";

            // ✅ Verify token (existing behavior)
            var ok = await _authService.VerifyEmailAsync(token);

            // If we have a frontend URL, redirect there with status
            if (!string.IsNullOrWhiteSpace(frontendVerifiedUrl))
            {
                var url = ok
                    ? $"{frontendVerifiedUrl}?verified=1"
                    : $"{frontendVerifiedUrl}?verified=0&reason=expired";

                return Redirect(url);
            }

            // ✅ Fallback: branded HTML page (no external CSS)
            // Try to load AppUrl for logo resolution (optional)
            // "AppUrl": "https://localhost:7033"
            var appUrl = (config?["AppUrl"] ?? "").Trim().TrimEnd('/');
            var logoUrl = string.IsNullOrWhiteSpace(appUrl) ? "" : $"{appUrl}/logo.png";

            var logoBlock = string.IsNullOrWhiteSpace(logoUrl)
                ? @"<div style=""font-size:22px;font-weight:800;letter-spacing:0.2px;color:#101828;"">Law<span style=""color:#801010;"">Africa</span></div>"
                : $@"<img src=""{System.Net.WebUtility.HtmlEncode(logoUrl)}"" alt=""LawAfrica"" width=""160"" style=""display:block;border:0;outline:none;text-decoration:none;height:auto;"" />";

            var title = ok ? "Email verified" : "Verification link expired";
            var heading = ok ? "Your email is verified" : "This verification link is invalid or expired";
            var body = ok
                ? "Your LawAfrica account email has been verified successfully. You can now sign in."
                : "For security, verification links expire. Please request a new verification email from the app.";

            // If we can infer a reasonable login URL, show it. Otherwise just show text.
            var suggestedLoginUrl = !string.IsNullOrWhiteSpace(frontendBaseUrl) ? $"{frontendBaseUrl}/login" : "";

            var buttonHtml = !string.IsNullOrWhiteSpace(suggestedLoginUrl) && ok
                ? $@"
            <tr>
              <td align=""left"" style=""padding-top:8px;"">
                <a href=""{System.Net.WebUtility.HtmlEncode(suggestedLoginUrl)}""
                   style=""display:inline-block;background:#801010;color:#ffffff;text-decoration:none;font-weight:700;
                          padding:12px 18px;border-radius:10px;font-size:14px;"">
                  Go to Login
                </a>
              </td>
            </tr>"
                : "";

            var html = $@"
            <!DOCTYPE html>
            <html lang=""en"">
            <head>
              <meta charset=""utf-8"" />
              <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
              <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"" />
              <title>LawAfrica | {title}</title>
            </head>
            <body style=""margin:0;padding:0;background:#F6F7FB;-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%;font-family:Arial,Helvetica,sans-serif;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#F6F7FB;border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;"">
                <tr>
                  <td align=""center"" style=""padding:24px 12px;"">

                    <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""680"" style=""width:680px;max-width:680px;background:#ffffff;border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;border-radius:16px;overflow:hidden;box-shadow:0 10px 30px rgba(16,24,40,0.08);"">
                      <tr>
                        <td style=""background:#801010;height:6px;line-height:6px;font-size:0;"">&nbsp;</td>
                      </tr>

                      <tr>
                        <td style=""padding:22px 24px 10px 24px;"">
                          <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;"">
                            <tr>
                              <td align=""left"" valign=""middle"">
                                {logoBlock}
                                <div style=""margin-top:6px;font-size:13px;line-height:18px;color:#667085;"">Know. Do. Be More</div>
                              </td>
                              <td align=""right"" valign=""middle"" style=""font-size:12px;color:#98A2B3;"">
                                Email verification
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style=""padding:8px 24px 24px 24px;"">
                          <div style=""font-size:22px;line-height:28px;font-weight:800;color:#101828;margin:0 0 12px 0;"">
                            {heading}
                          </div>

                          <div style=""font-size:14px;line-height:22px;color:#344054;margin:0 0 16px 0;"">
                            {body}
                          </div>

                          <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;"">
                            {buttonHtml}
                          </table>

                          <div style=""height:1px;background:#EAECF0;line-height:1px;font-size:0;margin:18px 0;"">&nbsp;</div>

                          <div style=""font-size:13px;line-height:20px;color:#667085;margin:0;"">
                            Need help? Contact <span style=""color:#1D4ED8;font-weight:700;"">support@lawafrica.com</span>.
                          </div>
                        </td>
                      </tr>

                      <tr>
                        <td style=""padding:16px 24px 22px 24px;background:#ffffff;"">
                          <div style=""font-size:12px;line-height:18px;color:#98A2B3;"">
                            &copy; {DateTime.UtcNow.Year} LawAfrica. All rights reserved.
                          </div>
                          <div style=""font-size:12px;line-height:18px;color:#98A2B3;margin-top:6px;"">
                            Please do not reply to this email.
                          </div>
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


        // =========================================================
        // SEND VERIFICATION EMAIL (safe)
        // =========================================================
        public class SendVerificationRequest
        {
            public string EmailOrUsername { get; set; } = string.Empty;
        }

        [AllowAnonymous]
        [HttpPost("send-verification")]
        public async Task<IActionResult> SendVerification([FromBody] SendVerificationRequest req)
        {
            var key = (req?.EmailOrUsername ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                return BadRequest("EmailOrUsername is required.");

            var lower = key.ToLowerInvariant();

            var user = await _db.Users
                .AsNoTracking()
                .Where(u =>
                    (!string.IsNullOrEmpty(u.Email) && u.Email.ToLower() == lower) ||
                    (!string.IsNullOrEmpty(u.Username) && u.Username.ToLower() == lower)
                )
                .Select(u => new { u.Id, u.IsEmailVerified })
                .FirstOrDefaultAsync();

            if (user == null)
                return Ok(new { message = "If the account exists, a verification email has been sent." });

            if (user.IsEmailVerified)
                return Ok(new { message = "Email is already verified." });

            try { await _authService.SendEmailVerificationAsync(user.Id); } catch { }

            return Ok(new { message = "If the account exists, a verification email has been sent." });
        }

        // =========================================================
        // RESEND EMAIL VERIFICATION (alias)
        // =========================================================
        public class ResendVerificationRequest
        {
            public string EmailOrUsername { get; set; } = string.Empty;
        }

        [AllowAnonymous]
        [HttpPost("resend-verification")]
        public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest req)
        {
            var key = (req?.EmailOrUsername ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                return BadRequest("EmailOrUsername is required.");

            var lower = key.ToLowerInvariant();

            var user = await _db.Users
                .AsNoTracking()
                .Where(u =>
                    (!string.IsNullOrEmpty(u.Email) && u.Email.ToLower() == lower) ||
                    (!string.IsNullOrEmpty(u.Username) && u.Username.ToLower() == lower)
                )
                .Select(u => new { u.Id, u.IsEmailVerified })
                .FirstOrDefaultAsync();

            if (user == null)
                return Ok(new { message = "If the account exists, a verification email has been sent." });

            if (user.IsEmailVerified)
                return Ok(new { message = "Email is already verified." });

            try { await _authService.SendEmailVerificationAsync(user.Id); } catch { }

            return Ok(new { message = "If the account exists, a verification email has been sent." });
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

        // =========================================================
        // PASSWORD RESET (clicked from email link)
        // GET: /api/Auth/reset-password?token=xxxx
        // =========================================================
        [AllowAnonymous]
        [HttpGet("reset-password")]
        public IActionResult ResetPasswordLink([FromQuery] string token)
        {
            token = (token ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest("Token is required.");

            var config = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;

            // ✅ Preferred: redirect to frontend reset page (best UX)
            var frontendResetUrl = (config?["FrontendResetPasswordUrl"] ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(frontendResetUrl))
            {
                // ✅ Safe append token whether url has ? already or not
                var separator = frontendResetUrl.Contains("?") ? "&" : "?";
                var url = $"{frontendResetUrl}{separator}token={Uri.EscapeDataString(token)}";
                return Redirect(url);
            }

            // ✅ Fallback: branded HTML page (no external CSS)
            var appUrl = (config?["AppUrl"] ?? "").Trim().TrimEnd('/');

            // If you serve logo from API wwwroot/logo.png:
            // e.g. https://localhost:7033/logo.png
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
                      <tr>
                        <td style=""background:#801010;height:6px;line-height:6px;font-size:0;"">&nbsp;</td>
                      </tr>

                      <tr>
                        <td style=""padding:22px 24px 10px 24px;"">
                          <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;"">
                            <tr>
                              <td align=""left"" valign=""middle"">
                                {logoBlock}
                                <div style=""margin-top:6px;font-size:13px;line-height:18px;color:#667085;"">Know. Do. Be More</div>
                              </td>
                              <td align=""right"" valign=""middle"" style=""font-size:12px;color:#98A2B3;"">
                                Password reset
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <tr>
                        <td style=""padding:8px 24px 24px 24px;"">
                          <div style=""font-size:22px;line-height:28px;font-weight:800;color:#101828;margin:0 0 12px 0;"">
                            Reset your password
                          </div>

                          <div style=""font-size:14px;line-height:22px;color:#344054;margin:0 0 16px 0;"">
                            You opened a password reset link. If your app did not automatically open,
                            copy the token below and paste it into the password reset form in the LawAfrica app.
                          </div>

                          <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;margin:0 0 14px 0;"">
                            <tr>
                              <td style=""background:#F9FAFB;border:1px solid #EAECF0;border-radius:12px;padding:14px;"">
                                <div style=""font-size:12px;line-height:18px;color:#667085;margin:0 0 8px 0;font-weight:700;"">
                                  Reset token
                                </div>
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
                          <div style=""font-size:12px;line-height:18px;color:#98A2B3;"">
                            &copy; {DateTime.UtcNow.Year} LawAfrica. All rights reserved.
                          </div>
                          <div style=""font-size:12px;line-height:18px;color:#98A2B3;margin-top:6px;"">
                            Please do not reply to this email.
                          </div>
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
