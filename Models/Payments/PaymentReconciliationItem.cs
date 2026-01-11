namespace LawAfrica.API.Models.Payments
{
    public class PaymentReconciliationItem
    {
        public long Id { get; set; }

        public long RunId { get; set; }
        public PaymentReconciliationRun Run { get; set; } = null!;

        public PaymentProvider Provider { get; set; }

        public string? Reference { get; set; }

        /// <summary>
        /// FK to PaymentIntent (nullable when provider tx has no intent)
        /// </summary>
        public int? PaymentIntentId { get; set; }
        public PaymentIntent? PaymentIntent { get; set; }

        /// <summary>
        /// FK to PaymentProviderTransaction (nullable when intent has no provider tx)
        /// </summary>
        public long? ProviderTransactionIdRef { get; set; }
        public PaymentProviderTransaction? ProviderTransaction { get; set; }

        public int? InvoiceId { get; set; }
        public Invoice? Invoice { get; set; }

        public ReconciliationStatus Status { get; set; }
        public ReconciliationReason Reason { get; set; }

        public string? Details { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
