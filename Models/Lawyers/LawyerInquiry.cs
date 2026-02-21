using System.ComponentModel.DataAnnotations;
using LawAfrica.API.Models.Locations;

namespace LawAfrica.API.Models.Lawyers
{
    public class LawyerInquiry
    {
        public int Id { get; set; }

        // Optional: directed to a specific lawyer
        public int? LawyerProfileId { get; set; }
        public LawyerProfile? LawyerProfile { get; set; }

        // ✅ Required: registered users only
        public int RequesterUserId { get; set; }
        public User RequesterUser { get; set; } = null!;

        // What they need help with
        public int? PracticeAreaId { get; set; }
        public PracticeArea? PracticeArea { get; set; }

        public int? TownId { get; set; }
        public Town? Town { get; set; }

        [Required, MaxLength(2000)]
        public string ProblemSummary { get; set; } = string.Empty;

        [MaxLength(80)]
        public string? PreferredContactMethod { get; set; } // "call", "email", etc.

        public InquiryStatus Status { get; set; } = InquiryStatus.New;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}