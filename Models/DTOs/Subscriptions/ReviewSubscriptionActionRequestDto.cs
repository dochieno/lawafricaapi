namespace LawAfrica.API.Models.DTOs.Subscriptions
{
    public class ReviewSubscriptionActionRequestDto
    {
        public bool Approve { get; set; }
        public string? Notes { get; set; }
    }
}
