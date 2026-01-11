using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Models.DTOs.Payments.Reconciliation
{
    public class ManualReconcileRequest
    {
        public int PaymentIntentId { get; set; }

        public PaymentProvider Provider { get; set; }

        // Provide at least one of these for matching/upsert
        public string? Reference { get; set; }
        public string? ProviderTransactionId { get; set; }

        public decimal Amount { get; set; }
        public string Currency { get; set; } = "KES";
        public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;

        public string? Channel { get; set; }
        public string? Notes { get; set; }
    }
}
