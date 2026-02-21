using LawAfrica.API.Data;
using LawAfrica.API.Models.Emails;
using LawAfrica.API.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Emails
{
    public class EmailOutboxWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<EmailOutboxWorker> _logger;
        private readonly string _workerId = $"email-worker-{Guid.NewGuid():N}".Substring(0, 18);

        public EmailOutboxWorker(IServiceProvider sp, ILogger<EmailOutboxWorker> logger)
        {
            _sp = sp;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _sp.CreateScope();
            var opts = scope.ServiceProvider.GetRequiredService<IConfiguration>()
                .GetSection("EmailOutbox")
                .Get<EmailOutboxOptions>() ?? new EmailOutboxOptions();

            if (!opts.Enabled)
            {
                _logger.LogWarning("[EMAIL OUTBOX] Disabled by configuration. Worker will not run.");
                return;
            }

            var poll = TimeSpan.FromSeconds(Math.Max(3, opts.PollSeconds));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessBatch(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[EMAIL OUTBOX] Worker loop error");
                }

                await Task.Delay(poll, stoppingToken);
            }
        }

        private async Task ProcessBatch(CancellationToken ct)
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
            var invoicePdf = scope.ServiceProvider.GetRequiredService<InvoicePdfService>();

            var now = DateTime.UtcNow;

            // grab a small batch ready to send
            var items = await db.EmailOutboxMessages
                .Where(x =>
                    (x.Status == EmailOutboxStatus.Pending || x.Status == EmailOutboxStatus.FailedRetrying) &&
                    x.NextAttemptAtUtc <= now)
                .OrderBy(x => x.Id)
                .Take(10)
                .ToListAsync(ct);

            foreach (var msg in items)
            {
                // Lock row (best-effort). If you want strong locking, use raw SQL with FOR UPDATE SKIP LOCKED.
                msg.Status = EmailOutboxStatus.Sending;
                msg.LockedAtUtc = now;
                msg.LockedBy = _workerId;
            }

            if (items.Count == 0) return;

            await db.SaveChangesAsync(ct);

            foreach (var msg in items)
            {
                try
                {
                    EmailAttachment[] attachments = Array.Empty<EmailAttachment>();

                    if (msg.AttachInvoicePdf && msg.InvoiceId.HasValue)
                    {
                        var (pdfBytes, fileName) = await invoicePdf.GenerateInvoicePdfWithFileNameAsync(msg.InvoiceId.Value, ct);
                        attachments = new[]
                        {
                            new EmailAttachment
                            {
                                FileName = fileName,
                                Bytes = pdfBytes,
                                ContentType = "application/pdf"
                            }
                        };
                    }

                    await emailService.SendEmailWithAttachmentsAsync(
                        msg.ToEmail,
                        msg.Subject,
                        msg.Html,
                        attachments,
                        ct);

                    msg.Status = EmailOutboxStatus.Sent;
                    msg.SentAtUtc = DateTime.UtcNow;
                    msg.LastError = null;

                    // mark invoice send-once fields too
                    if (msg.Kind == "InvoiceEmail" && msg.InvoiceId.HasValue)
                    {
                        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == msg.InvoiceId.Value, ct);
                        if (inv != null && !inv.PdfEmailedAt.HasValue)
                        {
                            inv.PdfEmailedAt = DateTime.UtcNow;
                            inv.PdfEmailedTo = msg.ToEmail;
                        }
                    }

                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    msg.AttemptCount += 1;
                    msg.LastError = ex.Message.Length > 1000 ? ex.Message.Substring(0, 1000) : ex.Message;

                    if (msg.AttemptCount >= 8)
                    {
                        msg.Status = EmailOutboxStatus.DeadLetter;
                        msg.NextAttemptAtUtc = DateTime.UtcNow.AddYears(100); // effectively never
                    }
                    else
                    {
                        msg.Status = EmailOutboxStatus.FailedRetrying;

                        // exponential-ish backoff
                        var delaySeconds = msg.AttemptCount switch
                        {
                            1 => 10,
                            2 => 30,
                            3 => 90,
                            4 => 180,
                            5 => 300,
                            _ => 600
                        };

                        msg.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
                    }

                    msg.LockedAtUtc = null;
                    msg.LockedBy = null;

                    await db.SaveChangesAsync(ct);

                    _logger.LogError(ex, "[EMAIL OUTBOX] Send failed id={Id} kind={Kind} attempt={Attempt}",
                        msg.Id, msg.Kind, msg.AttemptCount);
                }
            }
        }
    }
}
