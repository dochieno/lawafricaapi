using System;
using LawAfrica.API.Models;

namespace LawAfrica.API.Models.DTOs.Subscriptions
{
    public class InstitutionSubscriptionAuditDto
    {
        public int Id { get; set; }
        public int SubscriptionId { get; set; }

        public SubscriptionAuditAction Action { get; set; }
        public int? PerformedByUserId { get; set; }

        public DateTime OldStartDate { get; set; }
        public DateTime OldEndDate { get; set; }
        public SubscriptionStatus OldStatus { get; set; }

        public DateTime NewStartDate { get; set; }
        public DateTime NewEndDate { get; set; }
        public SubscriptionStatus NewStatus { get; set; }

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
