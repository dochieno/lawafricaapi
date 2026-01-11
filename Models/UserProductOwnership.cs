namespace LawAfrica.API.Models
{
    /// <summary>
    /// Represents permanent ownership of a content product by a user.
    /// </summary>
    public class UserProductOwnership
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int ContentProductId { get; set; }
        public ContentProduct ContentProduct { get; set; } = null!;

        public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
        public string? TransactionReference { get; set; }
    }
}
