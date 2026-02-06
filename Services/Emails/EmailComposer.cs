using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Emails;
using LawAfrica.API.Models.Payments;
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
        private readonly ILogger<EmailComposer> _logger;

        public EmailComposer(
            IConfiguration config,
            EmailService emailService,
            IEmailTemplateRenderer renderer,
            ApplicationDbContext db,
            InvoicePdfService invoicePdf,
            ILogger<EmailComposer> logger // ✅ add
        )
        {
            _config = config;
            _emailService = emailService;
            _renderer = renderer;
            _db = db;
            _invoicePdf = invoicePdf;
            _logger = logger; // ✅ add
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
        public async Task<bool> SendInvoiceEmailAsync(int invoiceId, CancellationToken ct = default)
        {
            var invoice = await _db.Invoices
                .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

            if (invoice == null)
            {
                _logger.LogWarning("[INVOICE EMAIL] Invoice not found InvoiceId={InvoiceId}", invoiceId);
                return false;
            }

            if (invoice.PdfEmailedAt.HasValue)
            {
                _logger.LogInformation("[INVOICE EMAIL] Skipped (already emailed) InvoiceId={InvoiceId} To={To} At={At}",
                    invoice.Id, invoice.PdfEmailedTo, invoice.PdfEmailedAt);
                return true;
            }

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

            // 2) Institution official email
            if (string.IsNullOrWhiteSpace(toEmail) && invoice.InstitutionId.HasValue && invoice.InstitutionId.Value > 0)
            {
                toEmail = await _db.Institutions
                    .AsNoTracking()
                    .Where(x => x.Id == invoice.InstitutionId.Value)
                    .Select(x => x.OfficialEmail)
                    .FirstOrDefaultAsync(ct);

                toEmail = toEmail?.Trim();
            }

            // 3) PublicSignupFee: PaymentIntent -> RegistrationIntent
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
            {
                _logger.LogWarning(
                    "[INVOICE EMAIL] Skipped: no recipient. InvoiceId={InvoiceId} UserId={UserId} InstitutionId={InstitutionId} Purpose={Purpose}",
                    invoice.Id, invoice.UserId, invoice.InstitutionId, invoice.Purpose);
                return false;
            }

            var displayName = !string.IsNullOrWhiteSpace(invoice.CustomerName)
                ? invoice.CustomerName!.Trim()
                : "there";

            byte[] pdfBytes;
            string fileName;

            try
            {
                (pdfBytes, fileName) = await _invoicePdf.GenerateInvoicePdfWithFileNameAsync(invoice.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[INVOICE EMAIL] PDF generation failed InvoiceId={InvoiceId}", invoice.Id);
                return false;
            }

            var invNo = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? $"INV-{invoice.Id}" : invoice.InvoiceNumber.Trim();
            var subject = $"{ProductName} Invoice {invNo}";
            var isPaid = invoice.PaidAt.HasValue && invoice.AmountPaid > 0m;
            var currency = (invoice.Currency ?? "KES").Trim();

            string html;
            string outSubject;

            try
            {
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
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[INVOICE EMAIL] Template render failed (fallback HTML used) InvoiceId={InvoiceId}", invoice.Id);
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

            try
            {
                _logger.LogInformation("[INVOICE EMAIL] Sending... InvoiceId={InvoiceId} To={To} Subject={Subject} PdfBytes={Bytes}",
                    invoice.Id, toEmail, outSubject, pdfBytes.Length);

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

                invoice.PdfEmailedAt = DateTime.UtcNow;
                invoice.PdfEmailedTo = toEmail;
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("[INVOICE EMAIL] Sent OK InvoiceId={InvoiceId} To={To}", invoice.Id, toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[INVOICE EMAIL] Send FAILED InvoiceId={InvoiceId} To={To}", invoice.Id, toEmail);
                return false;
            }
        }

        public async Task EnqueueInvoiceEmailAsync(int invoiceId, CancellationToken ct = default)
        {
            var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
            if (invoice == null) return;

            // Send-once guarantee (invoice-level)
            if (invoice.PdfEmailedAt.HasValue) return;

            // Resolve recipient (reuse your existing logic but DO NOT throw — store error in outbox instead)
            string? toEmail = await ResolveInvoiceRecipientEmailAsync(invoice, ct);
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                // Persist dead-letter immediately (actionable)
                _db.EmailOutboxMessages.Add(new EmailOutboxMessage
                {
                    Kind = "InvoiceEmail",
                    InvoiceId = invoice.Id,
                    ToEmail = "",
                    Subject = $"{ProductName} Invoice {invoice.InvoiceNumber ?? $"INV-{invoice.Id}"}",
                    Html = $"Invoice email skipped: no recipient email found. InvoiceId={invoice.Id}",
                    Status = EmailOutboxStatus.DeadLetter,
                    LastError = "No recipient email found",
                    NextAttemptAtUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(ct);
                return;
            }

            var invNo = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? $"INV-{invoice.Id}" : invoice.InvoiceNumber.Trim();
            var subject = $"{ProductName} Invoice {invNo}";

            var displayName = !string.IsNullOrWhiteSpace(invoice.CustomerName) ? invoice.CustomerName!.Trim() : "there";
            var isPaid = invoice.PaidAt.HasValue && invoice.AmountPaid > 0m;
            var currency = (invoice.Currency ?? "KES").Trim();

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

            _db.EmailOutboxMessages.Add(new EmailOutboxMessage
            {
                Kind = "InvoiceEmail",
                InvoiceId = invoice.Id,
                ToEmail = toEmail.Trim(),
                Subject = rendered.Subject,
                Html = rendered.Html,
                AttachInvoicePdf = true,
                Status = EmailOutboxStatus.Pending,
                NextAttemptAtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
        }

        // factor your recipient logic into a helper so you can reuse it
        private async Task<string?> ResolveInvoiceRecipientEmailAsync(Invoice invoice, CancellationToken ct)
        {
            // same logic you already had inside SendInvoiceEmailAsync (User -> Institution -> RegistrationIntent)
            // but return null instead of throw
            // ...
            return null;
        }


    }
}
