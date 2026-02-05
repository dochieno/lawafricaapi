using LawAfrica.API.Models;

namespace LawAfrica.API.Models.DTOs.Trials
{
    public class TrialApprovalResultDto
    {
        public bool Ok { get; set; } = true;
        public int RequestId { get; set; }

        public TrialSubscriptionDto Subscription { get; set; } = new();
    }

    public class TrialSubscriptionDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int ContentProductId { get; set; }
        public SubscriptionStatus Status { get; set; }
        public bool IsTrial { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
