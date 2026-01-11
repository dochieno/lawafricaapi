using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Payments
{
    /// <summary>
    /// Append-only store for provider webhook events (raw payload + processing outcome).
    /// Idempotent via Provider + DedupeHash unique constraint.
    /// </summary>
    public class PaymentProviderWebhookEvent
    {
        public long Id { get; set; }

        public PaymentProvider Provider { get; set; }

        // e.g. "charge.success"
        [MaxLength(100)]
        public string EventType { get; set; } = string.Empty;

        // Some providers provide a stable event id; Paystack may not.
        [MaxLength(150)]
        public string? ProviderEventId { get; set; }

        // SHA256(provider + rawBody) => unique per provider for safe retries
        [MaxLength(128)]
        public string DedupeHash { get; set; } = string.Empty;

        // Helpful for matching/debug
        [MaxLength(120)]
        public string? Reference { get; set; }

        public bool? SignatureValid { get; set; }

        public ProviderEventProcessingStatus ProcessingStatus { get; set; } = ProviderEventProcessingStatus.Received;

        [MaxLength(500)]
        public string? ProcessingError { get; set; }

        public string RawBody { get; set; } = string.Empty;

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
    }
}
