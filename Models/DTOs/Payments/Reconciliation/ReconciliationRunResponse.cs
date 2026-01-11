using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Models.DTOs.Payments.Reconciliation
{
    public class ReconciliationRunResponse
    {
        public long RunId { get; set; }

        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public PaymentProvider? Provider { get; set; }

        public int TotalItems { get; set; }
        public int Matched { get; set; }
        public int NeedsReview { get; set; }
        public int Mismatch { get; set; }
        public int MissingInternalIntent { get; set; }
        public int MissingProviderTransaction { get; set; }
        public int Duplicate { get; set; }
        public int FinalizerFailed { get; set; }
        public int ManuallyResolved { get; set; }
    }
}
