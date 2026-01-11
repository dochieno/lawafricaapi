using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Models.DTOs.AdminDashboard
{
    public class PaymentsKpiResponse
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        // Revenue totals (invoices)
        public decimal TotalRevenue { get; set; }
        public int PaidInvoices { get; set; }
        public decimal AverageInvoiceValue { get; set; }

        // Breakdowns
        public List<RevenueByProviderRow> RevenueByProvider { get; set; } = new();
        public List<RevenueByPurposeRow> RevenueByPurpose { get; set; } = new();

        // Health (reconciliation)
        public ReconciliationHealth Health { get; set; } = new();
    }

    public class RevenueByProviderRow
    {
        public PaymentProvider Provider { get; set; }
        public decimal Revenue { get; set; }
        public int Count { get; set; }
    }

    public class RevenueByPurposeRow
    {
        public PaymentPurpose Purpose { get; set; }
        public decimal Revenue { get; set; }
        public int Count { get; set; }
    }

    public class ReconciliationHealth
    {
        // 0-100 score
        public int Score { get; set; }

        public int TotalItems { get; set; }
        public int Matched { get; set; }
        public int NeedsReview { get; set; }
        public int Mismatch { get; set; }
        public int MissingInternalIntent { get; set; }
        public int MissingProviderTransaction { get; set; }
        public int Duplicate { get; set; }
        public int FinalizerFailed { get; set; }
        public int ManuallyResolved { get; set; }

        public string Summary { get; set; } = "";
    }
}
