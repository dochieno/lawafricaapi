using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models
{
    public enum TrialRequestStatus
    {
        Pending = 1,
        Approved = 2,
        Rejected = 3,
        Cancelled = 4
    }

    /// <summary>
    /// Tracks a user's request for a trial subscription.
    /// Approval is done ONLY by Global Admin.
    /// </summary>
    public class UserTrialSubscriptionRequest
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int ContentProductId { get; set; }
        public ContentProduct ContentProduct { get; set; } = null!;

        public TrialRequestStatus Status { get; set; } = TrialRequestStatus.Pending;

        [MaxLength(800)]
        public string? Reason { get; set; }

        [MaxLength(800)]
        public string? AdminNotes { get; set; }

        public int? ReviewedByUserId { get; set; }
        public User? ReviewedByUser { get; set; }

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAt { get; set; }

        // For audit/debug (optional)
        [MaxLength(64)]
        public string? RequestIp { get; set; }

        [MaxLength(256)]
        public string? UserAgent { get; set; }
    }
}
