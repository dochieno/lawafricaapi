using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Payments
{
    /// <summary>
    /// Normalized provider transaction record (latest known state).
    /// Populated primarily from server-to-server verification results.
    /// </summary>
    public class PaymentProviderTransaction
    {
        public long Id { get; set; }

        public PaymentProvider Provider { get; set; }

        // e.g. Paystack data.id
        [MaxLength(100)]
        public string ProviderTransactionId { get; set; } = string.Empty;

        // e.g. Paystack reference "LA-123-ABC123"
        [MaxLength(120)]
        public string Reference { get; set; } = string.Empty;

        public ProviderTransactionStatus Status { get; set; } = ProviderTransactionStatus.Unknown;

        public decimal Amount { get; set; }

        [MaxLength(10)]
        public string Currency { get; set; } = "KES";

        [MaxLength(50)]
        public string? Channel { get; set; }

        public DateTime? PaidAt { get; set; }

        // Store verified payload (json string). Keep trimmed.
        public string RawJson { get; set; } = string.Empty;

        public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    }
}
