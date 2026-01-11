using System;

namespace LawAfrica.API.Models
{
    public class InstitutionSubscriptionActionRequest
    {
        public int Id { get; set; }

        public int SubscriptionId { get; set; }
        public InstitutionProductSubscription Subscription { get; set; } = null!;

        public SubscriptionActionRequestType RequestType { get; set; }
        public SubscriptionActionRequestStatus Status { get; set; } = SubscriptionActionRequestStatus.Pending;

        public int RequestedByUserId { get; set; }
        public int? ReviewedByUserId { get; set; }

        public string? RequestNotes { get; set; }
        public string? ReviewNotes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAt { get; set; }
    }
}
