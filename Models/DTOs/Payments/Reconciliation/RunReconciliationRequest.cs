using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Models.DTOs.Payments.Reconciliation
{
    public class RunReconciliationRequest
    {
        public PaymentProvider? Provider { get; set; } // null => all
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
    }
}
