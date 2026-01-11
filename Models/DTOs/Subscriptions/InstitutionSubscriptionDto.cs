using LawAfrica.API.Models;

namespace LawAfrica.API.Models.DTOs.Subscriptions
{
    public class InstitutionSubscriptionDto
    {
        public int Id { get; set; }

        public int InstitutionId { get; set; }
        public string InstitutionName { get; set; } = string.Empty;

        public int ContentProductId { get; set; }
        public string ContentProductName { get; set; } = string.Empty;

        public SubscriptionStatus Status { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // ✅ NEW: pending request summary (for UX)
        public int? PendingRequestId { get; set; }
        public SubscriptionActionRequestType? PendingRequestType { get; set; }
        public DateTime? PendingRequestedAt { get; set; }
        public int? PendingRequestedByUserId { get; set; }
    }
}
