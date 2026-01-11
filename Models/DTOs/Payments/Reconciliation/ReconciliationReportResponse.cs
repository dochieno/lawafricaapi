using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Models.DTOs.Payments.Reconciliation
{
    public class ReconciliationReportResponse
    {
        public int Total { get; set; }
        public int Matched { get; set; }
        public int NeedsReview { get; set; }
        public int Mismatch { get; set; }
        public int MissingInternalIntent { get; set; }
        public int MissingProviderTransaction { get; set; }
        public int Duplicate { get; set; }
        public int FinalizerFailed { get; set; }
        public int ManuallyResolved { get; set; }

        public List<ReconciliationReportRow> Items { get; set; } = new();
    }

    public class ReconciliationReportRow
    {
        public long ItemId { get; set; }
        public long RunId { get; set; }
        public DateTime CreatedAt { get; set; }

        public PaymentProvider Provider { get; set; }
        public string? Reference { get; set; }

        public ReconciliationStatus Status { get; set; }
        public ReconciliationReason Reason { get; set; }
        public string? Details { get; set; }

        public int? PaymentIntentId { get; set; }
        public long? ProviderTransactionRowId { get; set; }
        public int? InvoiceId { get; set; }

        public decimal? IntentAmount { get; set; }
        public string? IntentCurrency { get; set; }
        public PaymentStatus? IntentStatus { get; set; }
        public bool? IntentFinalized { get; set; }
        public PaymentPurpose? Purpose { get; set; }

        public int? InstitutionId { get; set; }
        public int? UserId { get; set; }
    }
}
