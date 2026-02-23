using System;

namespace LawAfrica.API.DTOs.Lawyers
{
    // ✅ list DTO used by /mine and /for-me (keep shape stable)
    public class InquiryDto
    {
        public int Id { get; set; }
        public int? LawyerProfileId { get; set; }
        public int RequesterUserId { get; set; }

        public string ProblemSummary { get; set; } = string.Empty;
        public string Status { get; set; } = "New";
        public DateTime CreatedAt { get; set; }

        public string? PracticeAreaName { get; set; }
        public string? TownName { get; set; }

        // For lawyer-side view
        public string? RequesterName { get; set; }
        public string? RequesterPhone { get; set; }
        public string? RequesterEmail { get; set; }
    }

    // ✅ detail DTO used by GET /api/lawyers/inquiries/{id}
    public class InquiryDetailDto : InquiryDto
    {
        public string? PreferredContactMethod { get; set; }

        // Workflow fields
        public string? Outcome { get; set; }

        public DateTime? LastStatusChangedAtUtc { get; set; }
        public DateTime? ContactedAtUtc { get; set; }
        public DateTime? InProgressAtUtc { get; set; }
        public DateTime? ClosedAtUtc { get; set; }

        public int? ClosedByUserId { get; set; }
        public string? CloseNote { get; set; }

        // Rating
        public int? RatingStars { get; set; }
        public string? RatingComment { get; set; }
        public DateTime? RatedAtUtc { get; set; }

        // Lawyer snapshot (for requester view)
        public string? LawyerDisplayName { get; set; }
        public string? LawyerFirmName { get; set; }
        public string? LawyerPrimaryPhone { get; set; }
        public string? LawyerPublicEmail { get; set; }
    }
}