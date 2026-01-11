using System;

namespace LawAfrica.API.Models
{
    /// <summary>
    /// Audit trail for subscription lifecycle actions.
    /// </summary>
    public class InstitutionSubscriptionAudit
    {
        public int Id { get; set; }

        public int SubscriptionId { get; set; }
        public InstitutionProductSubscription Subscription { get; set; } = null!;

        public SubscriptionAuditAction Action { get; set; }

        public int? PerformedByUserId { get; set; }

        public DateTime OldStartDate { get; set; }
        public DateTime OldEndDate { get; set; }
        public SubscriptionStatus OldStatus { get; set; }

        public DateTime NewStartDate { get; set; }
        public DateTime NewEndDate { get; set; }
        public SubscriptionStatus NewStatus { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
