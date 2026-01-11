namespace LawAfrica.API.Models.Payments
{
    public class PaymentReconciliationRun
    {
        public long Id { get; set; }

        public PaymentProvider? Provider { get; set; }

        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public int PerformedByUserId { get; set; }
        public User? PerformedByUser { get; set; }

        /// <summary>
        /// Auto / Manual / Scheduled
        /// </summary>
        public string Mode { get; set; } = "Auto";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PaymentReconciliationItem> Items { get; set; } = new List<PaymentReconciliationItem>();
    }
}
