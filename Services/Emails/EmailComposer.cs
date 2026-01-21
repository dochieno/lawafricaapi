using LawAfrica.API.Models;
using LawAfrica.API.Models.Emails;
using Microsoft.Extensions.Configuration;

namespace LawAfrica.API.Services.Emails
{
    public class EmailComposer
    {
        private readonly IConfiguration _config;
        private readonly EmailService _emailService;
        private readonly IEmailTemplateRenderer _renderer;

        public EmailComposer(IConfiguration config, EmailService emailService, IEmailTemplateRenderer renderer)
        {
            _config = config;
            _emailService = emailService;
            _renderer = renderer;
        }

        private string ProductName => _config["Brand:ProductName"] ?? "LawAfrica";
        private string SupportEmail => _config["Brand:SupportEmail"] ?? _config["SupportEmail"] ?? "support@lawafrica.example";
        private string AppUrl => _config["AppUrl"] ?? "";

        private static string DisplayNameFor(User u)
        {
            var full = $"{u.FirstName} {u.LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(full)) return full;
            if (!string.IsNullOrWhiteSpace(u.FirstName)) return u.FirstName!;
            if (!string.IsNullOrWhiteSpace(u.Username)) return u.Username!;
            return "there";
        }

        public async Task SendEmailVerificationAsync(User user, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(user.Email)) return;

            var token = user.EmailVerificationToken ?? "";
            var verificationUrl = $"{AppUrl}/api/auth/verify-email?token={token}";

            var model = new
            {
                ProductName = ProductName,
                Year = DateTime.UtcNow.Year.ToString(),
                SupportEmail = SupportEmail,
                DisplayName = DisplayNameFor(user),
                VerificationUrl = verificationUrl
            };

            var rendered = await _renderer.RenderAsync(
                TemplateNames.EmailVerification,
                "Verify Your LawAfrica Account",
                model,
                ct: ct);

            await _emailService.SendEmailAsync(user.Email, rendered.Subject, rendered.Html);
        }

        public async Task SendTwoFactorSetupAsync(
            User user,
            string rawSetupToken,
            DateTime expiryUtc,
            byte[] qrBytes,
            string cid = "qrcode",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(user.Email)) return;

            var model = new
            {
                ProductName = ProductName,
                Year = DateTime.UtcNow.Year.ToString(),
                SupportEmail = SupportEmail,
                DisplayName = DisplayNameFor(user),
                SecretKey = user.TwoFactorSecret ?? "",
                SetupToken = rawSetupToken,
                SetupTokenExpiryUtc = expiryUtc.ToString("yyyy-MM-dd HH:mm") + " UTC",
                QrCid = cid
            };

            var rendered = await _renderer.RenderAsync(
                TemplateNames.TwoFactorSetup,
                "LawAfrica – Two-Factor Authentication Setup",
                model,
                inlineImages: new[]
                {
                    new EmailInlineImage{ ContentId = cid, Bytes = qrBytes, ContentType = "image/png", FileName = "qrcode.png" }
                },
                ct: ct);

            await _emailService.SendEmailWithInlineImageAsync(user.Email, rendered.Subject, rendered.Html, qrBytes, cid);
        }

        // ✅ NEW: Institution welcome email (access code)
        public async Task SendInstitutionWelcomeAsync(
            string toEmail,
            string institutionName,
            string emailDomain,
            string accessCode,
            bool requiresUserApproval,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(toEmail)) return;

            var regUrl = string.IsNullOrWhiteSpace(AppUrl) ? "" : $"{AppUrl.TrimEnd('/')}/register";

            // If your frontend uses a different route, override with config Brand:RegistrationUrl if you want
            var registrationUrl =
                _config["Brand:RegistrationUrl"] ??
                (string.IsNullOrWhiteSpace(regUrl) ? "https://app.lawafrica.example/register" : regUrl);

            var subject = $"Welcome to {ProductName} — Institution access code";

            var model = new
            {
                ProductName = ProductName,
                Year = DateTime.UtcNow.Year.ToString(),
                SupportEmail = SupportEmail,

                InstitutionName = institutionName,
                EmailDomain = emailDomain,
                OfficialEmail = toEmail,
                AccessCode = accessCode,

                RegistrationUrl = registrationUrl,

                // used in template sentence
                RequiresUserApproval = requiresUserApproval ? "true" : "false"
            };

            var rendered = await _renderer.RenderAsync(
                TemplateNames.InstitutionWelcome,
                subject,
                model,
                ct: ct);

            await _emailService.SendEmailAsync(toEmail, rendered.Subject, rendered.Html);
        }
    }
}
