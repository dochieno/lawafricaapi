namespace LawAfrica.API.Models
{
    /// <summary>
    /// Represents an institution-wide subscription to a content product.
    /// </summary>
    public class InstitutionProductSubscription
    {
        public int Id { get; set; }

        public int InstitutionId { get; set; }
        public Institution Institution { get; set; } = null!;

        public int ContentProductId { get; set; }
        public ContentProduct ContentProduct { get; set; } = null!;

        public SubscriptionStatus Status { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
