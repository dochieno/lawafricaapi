using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Emails
{
    public enum EmailOutboxStatus
    {
        Pending = 0,
        Sending = 1,
        Sent = 2,
        FailedRetrying = 3,
        DeadLetter = 4
    }

    public class EmailOutboxMessage
    {
        public int Id { get; set; }

        [MaxLength(100)]
        public string Kind { get; set; } = ""; // e.g. "InvoiceEmail"

        public int? InvoiceId { get; set; } // optional pointer

        [MaxLength(300)]
        public string ToEmail { get; set; } = "";

        [MaxLength(300)]
        public string Subject { get; set; } = "";

        public string Html { get; set; } = "";

        // If you want attachments durable, store bytes OR regenerate on send.
        // Best: regenerate invoice PDF by invoiceId at send-time (no bytes stored).
        public bool AttachInvoicePdf { get; set; } = false;

        public EmailOutboxStatus Status { get; set; } = EmailOutboxStatus.Pending;

        public int AttemptCount { get; set; } = 0;

        public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(1000)]
        public string? LastError { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? SentAtUtc { get; set; }
        public DateTime? LockedAtUtc { get; set; }

        [MaxLength(100)]
        public string? LockedBy { get; set; }
    }
}
