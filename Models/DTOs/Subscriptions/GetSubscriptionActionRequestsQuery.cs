namespace LawAfrica.API.Models.DTOs.Subscriptions
{
    public class GetSubscriptionActionRequestsQuery
    {
        // "all" | "pending" | "approved" | "rejected"
        public string? Status { get; set; }

        // optional search text
        public string? Q { get; set; }
    }
}
