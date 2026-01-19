using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs;
using LawAfrica.API.Models.DTOs.Security;
using LawAfrica.API.Models.Institutions;
using LawAfrica.API.Services.Emails; // ✅ renderer interface namespace
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OtpNet;
using QRCoder;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LawAfrica.API.Services
{
    public class AuthService
    {
        private readonly ApplicationDbContext _db;
        private readonly JwtSettings _jwt;
        private readonly IConfiguration _configuration;
        private readonly EmailService _emailService;
        private readonly IEmailTemplateRenderer _emailRenderer; // ✅ NEW
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly byte[] _keyBytes;
        private readonly SigningCredentials _signingCredentials;
        private static readonly Regex UsernameRegex = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

        private const int MAX_FAILED_ATTEMPTS = 2;
        private static readonly TimeSpan LOCKOUT_DURATION = TimeSpan.FromMinutes(15);

        private readonly SecurityAlertSettings _alertSettings;

        public AuthService(
            ApplicationDbContext db,
            IOptions<JwtSettings> jwtOptions,
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor,
            IOptions<SecurityAlertSettings> alertOptions,
            EmailService emailService,
            IEmailTemplateRenderer emailRenderer // ✅ NEW
        )
        {
            _db = db;
            _jwt = jwtOptions.Value ?? throw new ArgumentNullException(nameof(jwtOptions));
            _configuration = configuration;
            _emailService = emailService;
            _emailRenderer = emailRenderer; // ✅ NEW
            _httpContextAccessor = httpContextAccessor;
            _alertSettings = alertOptions.Value;

            var keyFromOptions = _jwt.Key ?? string.Empty;
            var keyFromConfig = _configuration["Jwt:Key"] ?? string.Empty;

            var effectiveKey = !string.IsNullOrWhiteSpace(keyFromOptions)
                ? keyFromOptions
                : keyFromConfig;

            if (string.IsNullOrWhiteSpace(effectiveKey))
                throw new InvalidOperationException("Jwt:Key is missing or empty. Configure a strong secret in appsettings/User Secrets/environment.");

            _keyBytes = Encoding.UTF8.GetBytes(effectiveKey);
            if (_keyBytes.Length < 32)
                throw new InvalidOperationException("Jwt:Key is too short. Use a 32+ character secret (recommended 256-bit).");

            // Keep _jwt consistent if options were empty
            if (string.IsNullOrWhiteSpace(_jwt.Key))
                _jwt.Key = effectiveKey;

            if (string.IsNullOrWhiteSpace(_jwt.Issuer))
                _jwt.Issuer = _configuration["Jwt:Issuer"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_jwt.Audience))
                _jwt.Audience = _configuration["Jwt:Audience"] ?? string.Empty;

            if (_jwt.DurationInMinutes <= 0)
            {
                var dur = _configuration["Jwt:DurationInMinutes"];
                if (int.TryParse(dur, out var parsed) && parsed > 0)
                    _jwt.DurationInMinutes = parsed;
                else
                    _jwt.DurationInMinutes = 60;
            }

            if (string.IsNullOrWhiteSpace(_jwt.Issuer))
                throw new InvalidOperationException("Jwt:Issuer is missing.");
            if (string.IsNullOrWhiteSpace(_jwt.Audience))
                throw new InvalidOperationException("Jwt:Audience is missing.");

            var signingKey = new SymmetricSecurityKey(_keyBytes);
            _signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        }

        // =========================================================
        // ✅ NEW: Central "2FA setup link" builder
        // =========================================================
        // Change: adds a single source of truth for the 2FA email link with token appended.
        // Env/appsettings: FrontendTwoFactorSetupUrl = https://lawafricadigitalhub.vercel.app/twofactor-setup
        private string BuildTwoFactorSetupLink(string rawSetupToken)
        {
            if (string.IsNullOrWhiteSpace(rawSetupToken))
                return string.Empty;

            // Preferred explicit page:
            var baseUrl = (_configuration["FrontendTwoFactorSetupUrl"] ?? "").Trim();

            // Fallback: build from FrontendUrl if you prefer that pattern
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                var frontendUrl = (_configuration["FrontendUrl"] ?? "").Trim().TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(frontendUrl))
                    baseUrl = $"{frontendUrl}/twofactor-setup";
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
                return string.Empty;

            baseUrl = baseUrl.TrimEnd('/');

            var sep = baseUrl.Contains("?") ? "&" : "?";
            return $"{baseUrl}{sep}token={Uri.EscapeDataString(rawSetupToken)}";
        }

        // =========================================================
        // CANONICAL 2FA HELPERS (FIXED)
        // =========================================================

        private static string NormalizeTotpCode(string code)
        {
            return Regex.Replace(code ?? "", @"\D", "");
        }

        private static string NormalizeBase32Secret(string secret)
        {
            return (secret ?? "")
                .Trim()
                .Replace(" ", "")
                .Replace("=", "")
                .ToUpperInvariant();
        }

        private static string GenerateTotpSecret()
        {
            var bytes = RandomNumberGenerator.GetBytes(20);
            return NormalizeBase32Secret(OtpNet.Base32Encoding.ToString(bytes));
        }

        private static string BuildQrCodeUri(User user)
        {
            var issuer = "LawAfrica";
            var account = string.IsNullOrWhiteSpace(user.Email) ? user.Username : user.Email;

            var encIssuer = Uri.EscapeDataString(issuer);
            var encAccount = Uri.EscapeDataString(account);

            var secret = NormalizeBase32Secret(user.TwoFactorSecret ?? "");

            return $"otpauth://totp/{encIssuer}:{encAccount}?secret={secret}&issuer={encIssuer}&digits=6";
        }

        private static bool ValidateTotp(string base32Secret, string code, int driftSteps = 2)
        {
            var secret = NormalizeBase32Secret(base32Secret);
            var cleanCode = NormalizeTotpCode(code);

            if (string.IsNullOrWhiteSpace(secret)) return false;
            if (cleanCode.Length != 6) return false;

            byte[] secretBytes;
            try
            {
                secretBytes = OtpNet.Base32Encoding.ToBytes(secret);
            }
            catch
            {
                return false;
            }

            var totp = new Totp(secretBytes, step: 30, totpSize: 6, mode: OtpHashMode.Sha1);
            var window = new VerificationWindow(previous: driftSteps, future: driftSteps);

            return totp.VerifyTotp(cleanCode, out _, window);
        }

        private static string Sha256Hex(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input ?? ""));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        // =========================================================
        // Step 1: Register new user
        // =========================================================

        public async Task<UserResponse?> RegisterAsync(RegisterRequest request)
        {
            if (request == null) return null;

            // ✅ normalize inputs early
            var username = (request.Username ?? "").Trim();
            var normalizedUsername = username.ToUpperInvariant();

            if (IsValid(request.Username))
                throw new ArgumentException(
                    "Username may contain letters and dots only (e.g. d.ochieno). Numbers and spaces are not allowed."
                );

            var email = (request.Email ?? "").Trim();
            var normalizedEmail = email;

            var password = request.Password ?? "";

            if (string.IsNullOrWhiteSpace(username)) return null;
            if (string.IsNullOrWhiteSpace(password)) return null;

            if (!IsStrongPassword(password))
                return null;

            var usernameTaken = await _db.Users.AnyAsync(u =>
                (u.NormalizedUsername != null && u.NormalizedUsername == normalizedUsername) ||
                (u.NormalizedUsername == null && u.Username.ToUpper() == normalizedUsername));

            if (usernameTaken)
                return null;

            if (!string.IsNullOrWhiteSpace(normalizedEmail))
            {
                var emailTaken = await _db.Users.AnyAsync(u => u.Email == normalizedEmail);
                if (emailTaken)
                    return null;
            }

            bool isFirstUser = !await _db.Users.AnyAsync();

            var user = new User
            {
                Username = username,
                NormalizedUsername = normalizedUsername,
                Email = normalizedEmail,
                PhoneNumber = request.PhoneNumber,
                FirstName = request.FirstName,
                LastName = request.LastName,
                CountryId = request.CountryId,
                City = request.City,

                Role = isFirstUser ? "Admin" : "User",
                RoleId = isFirstUser ? 1 : 2,

                IsActive = true,
                IsEmailVerified = false,

                TwoFactorEnabled = false,
                TwoFactorSecret = null,

                CreatedAt = DateTime.UtcNow
            };

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);

            user.EmailVerificationToken = GenerateVerificationToken();
            user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            try
            {
                await SendVerificationEmail(user);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Verification email failed: " + ex.Message);
            }

            return ToUserResponse(user);
        }

        // =========================================================
        // ✅ Send verification email for ANY user (used by registration intent flow)
        // =========================================================
        public async Task<bool> SendEmailVerificationAsync(int userId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return false;

            if (user.IsEmailVerified) return true;

            var needsNewToken =
                string.IsNullOrWhiteSpace(user.EmailVerificationToken) ||
                !user.EmailVerificationTokenExpiry.HasValue ||
                user.EmailVerificationTokenExpiry.Value <= DateTime.UtcNow;

            if (needsNewToken)
            {
                user.EmailVerificationToken = GenerateVerificationToken();
                user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);
                user.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();
            }

            await SendVerificationEmail(user);
            return true;
        }

        // =========================================================
        // Login (enforces 2FA before issuing token)
        // =========================================================
        public async Task<LoginResult> LoginAsync(LoginRequest request)
        {
            var identifier = (request.Username ?? "").Trim(); // accepts username OR email
            var password = request.Password ?? "";

            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(password))
                return LoginResult.Failed("Invalid credentials.");

            var identLower = identifier.ToLower();

            var user = await _db.Users.FirstOrDefaultAsync(u =>
                u.Username.ToLower() == identLower ||
                (u.Email != null && u.Email.ToLower() == identLower)
            );

            if (user == null)
                return LoginResult.Failed("Invalid credentials.");

            if (!user.IsActive)
                return LoginResult.Failed("Account is disabled.");

            if (!user.IsApproved)
                return LoginResult.Failed("Account not approved.");

            if (!user.IsEmailVerified)
                return LoginResult.Failed("Please verify your email before logging in. Check your inbox (and spam) for the verification link.");

            if (user.LockoutEndAt.HasValue && user.LockoutEndAt.Value > DateTime.UtcNow)
                return LoginResult.Failed("Account temporarily locked due to failed attempts.");

            if (string.IsNullOrWhiteSpace(user.PasswordHash) || !user.PasswordHash.StartsWith("$2"))
                return LoginResult.Failed("Account password is not set correctly. Contact support.");

            var passwordOk = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            if (!passwordOk)
            {
                await RegisterFailedLogin(user, "Invalid password");
                return LoginResult.Failed("Invalid credentials.");
            }

            if (string.IsNullOrWhiteSpace(user.TwoFactorSecret) || !user.TwoFactorEnabled)
                return LoginResult.TwoFactorSetupRequired(user.Id);

            return LoginResult.TwoFactorRequired(user.Id);
        }

        // ----------------------- GENERATE TOKEN -----------------------
        private string GenerateJwtToken(User user)
        {
            var role = user.Role ?? "User";

            bool isInstitutionAdmin = false;

            if (user.InstitutionId.HasValue)
            {
                var memberType = _db.InstitutionMemberships
                    .AsNoTracking()
                    .Where(m =>
                        m.UserId == user.Id &&
                        m.InstitutionId == user.InstitutionId.Value &&
                        m.Status == MembershipStatus.Approved &&
                        m.IsActive == true)
                    .Select(m => m.MemberType)
                    .FirstOrDefault();

                isInstitutionAdmin = memberType == InstitutionMemberType.InstitutionAdmin;
            }

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("userId", user.Id.ToString()),

                new Claim(ClaimTypes.Name, user.Username),
                new Claim("username", user.Username),

                new Claim(ClaimTypes.Role, role),
                new Claim("role", role),

                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim("phoneNumber", user.PhoneNumber ?? ""),
                new Claim("firstName", user.FirstName ?? ""),
                new Claim("lastName", user.LastName ?? ""),

                new Claim("institutionId", user.InstitutionId?.ToString() ?? ""),

                new Claim("userType", user.UserType.ToString()),
                new Claim("isApproved", user.IsApproved.ToString().ToLowerInvariant()),

                new Claim("isGlobalAdmin", user.IsGlobalAdmin.ToString().ToLowerInvariant()),
                new Claim("isInstitutionAdmin", isInstitutionAdmin.ToString().ToLowerInvariant()),
                new Claim("institutionRole", isInstitutionAdmin ? "InstitutionAdmin" : ""),
            };

            if (isInstitutionAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "InstitutionAdmin"));
            }

            var minutes = _jwt.DurationInMinutes > 0 ? _jwt.DurationInMinutes : 60;
            var now = DateTime.UtcNow;

            var token = new JwtSecurityToken(
                issuer: _jwt.Issuer,
                audience: _jwt.Audience,
                claims: claims,
                notBefore: now,
                expires: now.AddMinutes(minutes),
                signingCredentials: _signingCredentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // =========================================================
        // ✅ Branded template-based verification email
        // =========================================================
        private async Task SendVerificationEmail(User user)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
                return;

            var token = (user.EmailVerificationToken ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token))
                return;

            var apiBaseUrl =
                (_configuration["ApiBaseUrl"] ?? "").Trim().TrimEnd('/') ??
                "";

            if (string.IsNullOrWhiteSpace(apiBaseUrl))
                apiBaseUrl = (_configuration["AppUrl"] ?? "").Trim().TrimEnd('/');

            if (string.IsNullOrWhiteSpace(apiBaseUrl))
                apiBaseUrl = "https://lawafricaapi.onrender.com";

            var verificationUrl =
                $"{apiBaseUrl}/api/auth/verify-email?token={Uri.EscapeDataString(token)}";

            var subject = "Welcome to LawAfrica — verify your email ✅";
            var preheader = "You’re in! Verify your email to unlock your LawAfrica account.";

            var rendered = await _emailRenderer.RenderAsync(
                templateName: "email-verification",
                subject: subject,
                model: new
                {
                    Subject = subject,
                    Preheader = preheader,
                    ProductName = "LawAfrica",
                    Year = DateTime.UtcNow.Year.ToString(),
                    SupportEmail = "support@lawafrica.com",

                    DisplayName = (user.FirstName ?? user.Username),
                    VerificationUrl = verificationUrl,

                    WelcomeTitle = "Welcome to LawAfrica 🎉",
                    WelcomeBody =
                        "You’re one step away from getting full access. Verify your email to activate your account, then sign in and start exploring legal resources, updates, and your personal library."
                },
                inlineImages: null,
                ct: CancellationToken.None
            );

            await _emailService.SendEmailAsync(user.Email, rendered.Subject ?? subject, rendered.Html);
        }

        // =========================================================
        // ✅ Branded template-based password reset email
        // =========================================================
        private async Task SendPasswordResetEmail(User user)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
                return;

            var appUrl = (_configuration["AppUrl"] ?? "").Trim().TrimEnd('/');
            var token = user.PasswordResetToken ?? string.Empty;
            var resetUrl = $"{appUrl}/api/auth/reset-password?token={Uri.EscapeDataString(token)}";

            var subject = "Reset your LawAfrica password";

            var rendered = await _emailRenderer.RenderAsync(
                templateName: "password-reset",
                subject: subject,
                model: new
                {
                    Subject = subject,
                    Preheader = "Reset your password using the secure link inside.",
                    ProductName = "LawAfrica",
                    Year = DateTime.UtcNow.Year.ToString(),
                    SupportEmail = "support@lawafrica.com",

                    DisplayName = (user.FirstName ?? user.Username),
                    ResetUrl = resetUrl
                },
                inlineImages: null,
                ct: CancellationToken.None
            );

            await _emailService.SendEmailAsync(user.Email, rendered.Subject ?? subject, rendered.Html);
        }

        public async Task<bool> RequestPasswordResetAsync(string email)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
                return true;

            var tokenBytes = RandomNumberGenerator.GetBytes(64);
            var token = Convert.ToBase64String(tokenBytes);

            user.PasswordResetToken = token;
            user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(1);
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await SendPasswordResetEmail(user);

            return true;
        }

        public async Task<bool> ResetPasswordAsync(string token, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var user = await _db.Users.FirstOrDefaultAsync(u =>
                u.PasswordResetToken == token &&
                u.PasswordResetTokenExpiry > DateTime.UtcNow);

            if (user == null)
                return false;

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiry = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> VerifyEmailAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.EmailVerificationToken == token);
            if (user == null)
                return false;

            if (user.EmailVerificationTokenExpiry < DateTime.UtcNow)
                return false;

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiry = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ResendVerificationAsync(int userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null || user.IsEmailVerified)
                return false;

            user.EmailVerificationToken = GenerateVerificationToken();
            user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await SendVerificationEmail(user);

            return true;
        }

        private static UserResponse ToUserResponse(User user)
        {
            return new UserResponse
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Country = user.Country?.Name,
                City = user.City,
                Role = user.Role,
                IsActive = user.IsActive,
                IsEmailVerified = user.IsEmailVerified,
                CreatedAt = user.CreatedAt
            };
        }

        private string GenerateVerificationToken()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                .Replace("=", "")
                .Replace("+", "")
                .Replace("/", "");
        }

        // =========================================================
        // Enable Two-Factor Authentication (2FA)
        // =========================================================
        public async Task<TwoFactorSetupResponse> EnableTwoFactorAuthAsync(int userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) throw new Exception("User not found");

            if (string.IsNullOrWhiteSpace(user.TwoFactorSecret))
            {
                user.TwoFactorSecret = GenerateTotpSecret();
                user.TwoFactorEnabled = false;
            }

            user.TwoFactorSecret = NormalizeBase32Secret(user.TwoFactorSecret);

            var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
            var rawSetupToken = WebEncoders.Base64UrlEncode(rawTokenBytes);

            user.TwoFactorSetupTokenHash = Sha256Hex(rawSetupToken);
            user.TwoFactorSetupTokenExpiry = DateTime.UtcNow.AddMinutes(30);
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            var otpauth = BuildQrCodeUri(user);

            var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(otpauth, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrData);
            var qrBytes = qrCode.GetGraphic(20);

            var cid = "qrcode";
            var expiryUtc = user.TwoFactorSetupTokenExpiry!.Value;
            var secretKey = user.TwoFactorSecret!;

            var subject = "LawAfrica – Two-Factor Authentication Setup";

            // ✅ NEW: include a link to frontend setup page with token appended
            var setupLink = BuildTwoFactorSetupLink(rawSetupToken); // ✅ CHANGED

            var rendered = await _emailRenderer.RenderAsync(
                templateName: "twofactor-setup",
                subject: subject,
                model: new
                {
                    Subject = subject,
                    Preheader = "Scan the QR code to enable 2FA on your account.",
                    ProductName = "LawAfrica",
                    Year = DateTime.UtcNow.Year.ToString(),
                    SupportEmail = "support@lawafrica.com",

                    DisplayName = (user.FirstName ?? user.Username),
                    QrCid = cid,
                    SecretKey = secretKey,
                    SetupToken = rawSetupToken,
                    SetupLink = setupLink, // ✅ NEW (use in template)
                    SetupTokenExpiryUtc = $"{expiryUtc:yyyy-MM-dd HH:mm} UTC"
                },
                inlineImages: null,
                ct: CancellationToken.None
            );

            await _emailService.SendEmailWithInlineImageAsync(
                user.Email,
                rendered.Subject ?? subject,
                rendered.Html,
                qrBytes,
                cid
            );

            return new TwoFactorSetupResponse
            {
                Secret = secretKey,
                QrCodeUri = otpauth,
                SetupToken = rawSetupToken,
                SetupTokenExpiryUtc = expiryUtc
            };
        }

        public async Task<bool> VerifyTwoFactorSetupByTokenAsync(string setupToken, string code)
        {
            if (string.IsNullOrWhiteSpace(setupToken) || string.IsNullOrWhiteSpace(code))
                return false;

            var tokenHash = Sha256Hex(setupToken);

            var user = await _db.Users.FirstOrDefaultAsync(u =>
                u.TwoFactorSetupTokenHash == tokenHash &&
                u.TwoFactorSetupTokenExpiry != null &&
                u.TwoFactorSetupTokenExpiry > DateTime.UtcNow);

            if (user == null) return false;
            if (string.IsNullOrWhiteSpace(user.TwoFactorSecret)) return false;

            var ok = ValidateTotp(user.TwoFactorSecret, code, driftSteps: 5);
            if (!ok) return false;

            user.TwoFactorEnabled = true;
            user.TwoFactorSetupTokenHash = null;
            user.TwoFactorSetupTokenExpiry = null;

            // ✅ Since the QR/setup token was delivered to this email, treat email as verified now.
            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiry = null;

            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<(bool ok, string? token, string? error)> VerifyTwoFactorLoginAsync(string username, string code)
        {
            var ident = (username ?? "").Trim();
            var cleanCode = (code ?? "").Trim();

            if (string.IsNullOrWhiteSpace(ident) || string.IsNullOrWhiteSpace(cleanCode))
                return (false, null, "Username or email and code are required.");

            var identUpper = ident.ToUpperInvariant();
            var identLower = ident.ToLowerInvariant();

            var user = await _db.Users.FirstOrDefaultAsync(u =>
                (!string.IsNullOrWhiteSpace(u.NormalizedUsername) && u.NormalizedUsername == identUpper) ||
                (!string.IsNullOrWhiteSpace(u.Email) && u.Email.Trim().ToLower() == identLower)
            );

            if (user == null)
                return (false, null, "User not found.");

            if (!user.IsActive)
                return (false, null, "Account disabled.");

            if (!user.IsApproved)
                return (false, null, "Account not approved.");

            if (!user.TwoFactorEnabled || string.IsNullOrWhiteSpace(user.TwoFactorSecret))
                return (false, null, "2FA not enabled.");

            var ok = ValidateTotp(user.TwoFactorSecret, cleanCode, driftSteps: 5);
            if (!ok)
            {
                await RegisterFailedLogin(user, "Invalid 2FA code");
                return (false, null, "Invalid 2FA code.");
            }

            user.LastLoginAt = DateTime.UtcNow;
            user.FailedLoginAttempts = 0;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (true, GenerateJwtToken(user), null);
        }

        public async Task<SecurityStatusResponse?> GetSecurityStatusAsync(int userId)
        {
            var user = await _db.Users
                .Include(u => u.Country)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return null;

            return new SecurityStatusResponse
            {
                UserId = user.Id,
                Username = user.Username,
                Email = user.Email,
                IsEmailVerified = user.IsEmailVerified,
                TwoFactorEnabled = user.TwoFactorEnabled,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                Role = user.Role,
                Country = user.Country?.Name,
                City = user.City
            };
        }

        public async Task<bool> DisableTwoFactorAsync(int userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null)
                return false;

            user.TwoFactorEnabled = false;
            user.TwoFactorSecret = null;
            user.TwoFactorSetupTokenHash = null;
            user.TwoFactorSetupTokenExpiry = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<TwoFactorSetupResponse> RegenerateTwoFactorAuthAsync(int userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null)
                throw new Exception("User not found");

            user.TwoFactorSecret = GenerateTotpSecret();
            user.TwoFactorEnabled = false;

            var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
            var rawSetupToken = WebEncoders.Base64UrlEncode(rawTokenBytes);

            user.TwoFactorSetupTokenHash = Sha256Hex(rawSetupToken);
            user.TwoFactorSetupTokenExpiry = DateTime.UtcNow.AddMinutes(30);
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            var otpauth = BuildQrCodeUri(user);

            var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(otpauth, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrData);
            var qrBytes = qrCode.GetGraphic(20);

            var cid = "qrcode";
            var expiryUtc = user.TwoFactorSetupTokenExpiry!.Value;

            var subject = "LawAfrica – Two-Factor Authentication Setup";

            // ✅ NEW: include a link to frontend setup page with token appended
            var setupLink = BuildTwoFactorSetupLink(rawSetupToken); // ✅ CHANGED

            var rendered = await _emailRenderer.RenderAsync(
                templateName: "twofactor-setup",
                subject: subject,
                model: new
                {
                    Subject = subject,
                    Preheader = "Scan the QR code to enable 2FA on your account.",
                    ProductName = "LawAfrica",
                    Year = DateTime.UtcNow.Year.ToString(),
                    SupportEmail = "support@lawafrica.com",

                    DisplayName = (user.FirstName ?? user.Username),
                    QrCid = cid,
                    SecretKey = user.TwoFactorSecret!,
                    SetupToken = rawSetupToken,
                    SetupLink = setupLink, // ✅ NEW (use in template)
                    SetupTokenExpiryUtc = $"{expiryUtc:yyyy-MM-dd HH:mm} UTC"
                },
                inlineImages: null,
                ct: CancellationToken.None
            );

            await _emailService.SendEmailWithInlineImageAsync(
                user.Email,
                rendered.Subject ?? subject,
                rendered.Html,
                qrBytes,
                cid
            );

            return new TwoFactorSetupResponse
            {
                Secret = user.TwoFactorSecret!,
                QrCodeUri = otpauth,
                SetupToken = rawSetupToken,
                SetupTokenExpiryUtc = expiryUtc
            };
        }

        private async Task RegisterFailedLogin(User user, string reason)
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts > MAX_FAILED_ATTEMPTS)
            {
                user.LockoutEndAt = DateTime.UtcNow.Add(LOCKOUT_DURATION);
                user.FailedLoginAttempts = 0;
            }

            _db.LoginAudits.Add(new LoginAudit
            {
                UserId = user.Id,
                IsSuccessful = false,
                FailureReason = reason,
                IpAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown",
                UserAgent = _httpContextAccessor.HttpContext?.Request?.Headers["User-Agent"].ToString() ?? "Unknown"
            });

            await _db.SaveChangesAsync();
        }

        private async Task RegisterSuccessfulLogin(User user)
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEndAt = null;
            user.LastLoginAt = DateTime.UtcNow;

            _db.LoginAudits.Add(new LoginAudit
            {
                UserId = user.Id,
                IsSuccessful = true,
                IpAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown",
                UserAgent = _httpContextAccessor.HttpContext?.Request?.Headers["User-Agent"].ToString() ?? "Unknown"
            });

            await _db.SaveChangesAsync();
        }

        private async Task SendSuspiciousActivityAlert(User user)
        {
            var html = $@"
                <h2>Security Alert</h2>
                <p>Dear {user.FirstName ?? user.Username},</p>
                <p>We detected multiple failed login attempts on your account.</p>
                <p><b>Account:</b> {user.Email}<br/>
                   <b>Time:</b> {DateTime.UtcNow:u}</p>
                <p>If this was not you, we strongly recommend changing your password.</p>";

            await _emailService.SendEmailAsync(user.Email, "⚠️ LawAfrica Security Alert", html);

            if (!string.IsNullOrEmpty(_alertSettings.AdminAlertEmail))
            {
                await _emailService.SendEmailAsync(
                    _alertSettings.AdminAlertEmail,
                    "🚨 Suspicious Login Activity Detected",
                    $"User {user.Email} has multiple failed login attempts."
                );
            }
        }

        private async Task SendAccountLockoutAlert(User user)
        {
            var html = $@"
                <h2>Account Locked</h2>
                <p>Hello {user.FirstName ?? user.Username},</p>
                <p>Your account has been temporarily locked due to multiple failed login attempts.</p>
                <p><b>Lockout until:</b> {user.LockoutEndAt:u}</p>
                <p>If this was not you, please contact support immediately.</p>";

            await _emailService.SendEmailAsync(user.Email, "🔒 Your LawAfrica Account Is Locked", html);
        }

        public async Task<TwoFactorSetupResponse?> ResendTwoFactorSetupAsync(string username, string password)
        {
            var normalized = username.Trim().ToLower();

            var user = await _db.Users.FirstOrDefaultAsync(u =>
                u.Username.ToLower() == normalized || u.Email.ToLower() == normalized);

            if (user == null)
                return null;

            if (!user.IsActive)
                return null;

            if (user.LockoutEndAt.HasValue && user.LockoutEndAt.Value > DateTime.UtcNow)
                return null;

            var ok = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            if (!ok)
                return null;

            if (string.IsNullOrWhiteSpace(user.TwoFactorSecret))
            {
                user.TwoFactorSecret = GenerateTotpSecret();
                user.TwoFactorEnabled = false;
            }

            var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
            var rawSetupToken = WebEncoders.Base64UrlEncode(rawTokenBytes);

            user.TwoFactorSetupTokenHash = Sha256Hex(rawSetupToken);
            user.TwoFactorSetupTokenExpiry = DateTime.UtcNow.AddHours(1);
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            var otpauth = BuildQrCodeUri(user);

            var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(otpauth, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrData);
            var qrBytes = qrCode.GetGraphic(20);

            var cid = "qrcode";
            var expiryUtc = user.TwoFactorSetupTokenExpiry!.Value;

            var subject = "LawAfrica – Two-Factor Authentication Setup";

            // ✅ NEW: same logic as Enable/Regenerate — append token to setup page link
            var setupLink = BuildTwoFactorSetupLink(rawSetupToken); // ✅ CHANGED

            var rendered = await _emailRenderer.RenderAsync(
                templateName: "twofactor-setup",
                subject: subject,
                model: new
                {
                    Subject = subject,
                    Preheader = "Scan the QR code to enable 2FA on your account.",
                    ProductName = "LawAfrica",
                    Year = DateTime.UtcNow.Year.ToString(),
                    SupportEmail = "support@lawafrica.com",

                    DisplayName = (user.FirstName ?? user.Username),
                    QrCid = cid,
                    SecretKey = user.TwoFactorSecret!,
                    SetupToken = rawSetupToken,
                    SetupLink = setupLink, // ✅ NEW (use in template)
                    SetupTokenExpiryUtc = $"{expiryUtc:yyyy-MM-dd HH:mm} UTC"
                },
                inlineImages: null,
                ct: CancellationToken.None
            );

            await _emailService.SendEmailWithInlineImageAsync(
                user.Email,
                rendered.Subject ?? subject,
                rendered.Html,
                qrBytes,
                cid
            );

            return new TwoFactorSetupResponse
            {
                Secret = user.TwoFactorSecret!,
                QrCodeUri = otpauth,
                SetupToken = rawSetupToken,
                SetupTokenExpiryUtc = expiryUtc
            };
        }

        private static string GenerateSetupToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

        // =========================
        // Helpers
        // =========================
        private static bool IsStrongPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password)) return false;
            if (password.Length < 8) return false;

            bool hasUpper = password.Any(char.IsUpper);
            bool hasLower = password.Any(char.IsLower);
            bool hasDigit = password.Any(char.IsDigit);
            bool hasSpecial = password.Any(ch => !char.IsLetterOrDigit(ch));

            return hasUpper && hasLower && hasDigit && hasSpecial;
        }

        public static bool IsValid(string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            if (!string.Equals(username, username.Trim(), StringComparison.Ordinal))
                return false;

            return UsernameRegex.IsMatch(username);
        }
    }
}
