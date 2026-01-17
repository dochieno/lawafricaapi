namespace LawAfrica.API.Models
{
    /// <summary>
    /// Represents a subscription to a content product by a user.
    /// </summary>
    public class UserProductSubscription
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int ContentProductId { get; set; }
        public ContentProduct ContentProduct { get; set; } = null!;

        public SubscriptionStatus Status { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsTrial { get; set; } = false;

        public int? GrantedByUserId { get; set; }
        public User? GrantedByUser { get; set; }

    }
}
