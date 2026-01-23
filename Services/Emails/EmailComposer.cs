using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Emails;
using LawAfrica.API.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LawAfrica.API.Services.Emails
{
    public class EmailComposer
    {
        private readonly IConfiguration _config;
        private readonly EmailService _emailService;
        private readonly IEmailTemplateRenderer _renderer;
        private readonly ApplicationDbContext _db;
        private readonly InvoicePdfService _invoicePdf;

        public EmailComposer(
            IConfiguration config,
            EmailService emailService,
            IEmailTemplateRenderer renderer,
            ApplicationDbContext db,
            InvoicePdfService invoicePdf)
        {
            _config = config;
            _emailService = emailService;
            _renderer = renderer;
            _db = db;
            _invoicePdf = invoicePdf;
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

        // ===========================
        // Existing methods kept
        // ===========================
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
                RequiresUserApproval = requiresUserApproval ? "true" : "false"
            };

            var rendered = await _renderer.RenderAsync(
                TemplateNames.InstitutionWelcome,
                subject,
                model,
                ct: ct);

            await _emailService.SendEmailAsync(toEmail, rendered.Subject, rendered.Html);
        }

        // ===========================
        // ✅ Invoice PDF email (SEND-ONCE + TEMPLATE-FIX)
        // ===========================
        public async Task SendInvoiceEmailAsync(int invoiceId, CancellationToken ct = default)
        {
            // ✅ Tracked invoice (we must update PdfEmailedAt/PdfEmailedTo)
            var invoice = await _db.Invoices
                .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

            if (invoice == null)
                return;

            // ✅ Send-once guarantee
            if (invoice.PdfEmailedAt.HasValue)
                return;

            // ✅ Resolve recipient email robustly
            string? toEmail = null;

            // 1) User email
            if (invoice.UserId.HasValue && invoice.UserId.Value > 0)
            {
                toEmail = await _db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == invoice.UserId.Value)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync(ct);

                toEmail = toEmail?.Trim();
            }

            // 2) Institution official email (adjust if you later add BillingEmail)
            if (string.IsNullOrWhiteSpace(toEmail) && invoice.InstitutionId.HasValue && invoice.InstitutionId.Value > 0)
            {
                toEmail = await _db.Institutions
                    .AsNoTracking()
                    .Where(x => x.Id == invoice.InstitutionId.Value)
                    .Select(x => x.OfficialEmail)
                    .FirstOrDefaultAsync(ct);

                toEmail = toEmail?.Trim();
            }

            // 3) PublicSignupFee path: PaymentIntent -> RegistrationIntent
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                var regId = await _db.PaymentIntents
                    .AsNoTracking()
                    .Where(p => p.InvoiceId == invoice.Id && p.RegistrationIntentId != null)
                    .OrderByDescending(p => p.Id)
                    .Select(p => p.RegistrationIntentId)
                    .FirstOrDefaultAsync(ct);

                if (regId.HasValue && regId.Value > 0)
                {
                    toEmail = await _db.RegistrationIntents
                        .AsNoTracking()
                        .Where(r => r.Id == regId.Value)
                        .Select(r => r.Email)
                        .FirstOrDefaultAsync(ct);

                    toEmail = toEmail?.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(toEmail))
                throw new InvalidOperationException(
                    $"Invoice email skipped: no recipient email found. InvoiceId={invoice.Id}, UserId={invoice.UserId}, InstitutionId={invoice.InstitutionId}");

            var displayName = !string.IsNullOrWhiteSpace(invoice.CustomerName)
                ? invoice.CustomerName!.Trim()
                : "there";

            var (pdfBytes, fileName) = await _invoicePdf.GenerateInvoicePdfWithFileNameAsync(invoice.Id, ct);

            var invNo = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? $"INV-{invoice.Id}" : invoice.InvoiceNumber.Trim();
            var subject = $"{ProductName} Invoice {invNo}";

            var isPaid = invoice.PaidAt.HasValue && invoice.AmountPaid > 0m;
            var currency = (invoice.Currency ?? "KES").Trim();

            string html;
            string outSubject;

            try
            {
                // ✅ Matches your invoice-email template placeholders
                var model = new
                {
                    ProductName = ProductName,
                    Year = DateTime.UtcNow.Year.ToString(),
                    SupportEmail = SupportEmail,
                    DisplayName = displayName,

                    InvoiceNumber = invNo,
                    InvoiceStatus = invoice.Status.ToString(),
                    IssuedAt = invoice.IssuedAt.ToString("dd-MMM-yyyy"),

                    Currency = currency,
                    Subtotal = invoice.Subtotal.ToString("0,0.00"),
                    TaxTotal = invoice.TaxTotal.ToString("0,0.00"),
                    Total = invoice.Total.ToString("0,0.00"),

                    PaidBlock = isPaid,
                    AmountPaid = invoice.AmountPaid.ToString("0,0.00"),
                    PaidAt = isPaid ? invoice.PaidAt!.Value.ToString("dd-MMM-yyyy HH:mm") : ""
                };

                var rendered = await _renderer.RenderAsync(TemplateNames.InvoiceEmail, subject, model, ct: ct);
                outSubject = rendered.Subject;
                html = rendered.Html;
            }
            catch
            {
                outSubject = subject;

                var paidLine = isPaid
                    ? $"{currency} {invoice.AmountPaid:0,0.00} on {invoice.PaidAt!.Value:dd-MMM-yyyy HH:mm}"
                    : "—";

                html = $@"
                    <div style='font-family:Arial,sans-serif;font-size:14px;line-height:1.5'>
                      <p>Hi {System.Net.WebUtility.HtmlEncode(displayName)},</p>
                      <p>Please find your invoice attached.</p>
                      <p>
                        <b>Invoice:</b> {System.Net.WebUtility.HtmlEncode(invNo)}<br/>
                        <b>Status:</b> {System.Net.WebUtility.HtmlEncode(invoice.Status.ToString())}<br/>
                        <b>Total:</b> {System.Net.WebUtility.HtmlEncode(currency)} {invoice.Total:0,0.00}<br/>
                        <b>Paid:</b> {System.Net.WebUtility.HtmlEncode(paidLine)}
                      </p>
                      <p>Support: {System.Net.WebUtility.HtmlEncode(SupportEmail)}</p>
                    </div>";
            }

            // ✅ Send email (throws if Graph fails)
            await _emailService.SendEmailWithAttachmentsAsync(
                toEmail,
                outSubject,
                html,
                attachments: new[]
                {
                    new EmailAttachment
                    {
                        FileName = fileName,
                        Bytes = pdfBytes,
                        ContentType = "application/pdf"
                    }
                },
                ct: ct);

            // ✅ Mark as emailed (send-once durable)
            invoice.PdfEmailedAt = DateTime.UtcNow;
            invoice.PdfEmailedTo = toEmail;

            await _db.SaveChangesAsync(ct);
        }
    }
}
