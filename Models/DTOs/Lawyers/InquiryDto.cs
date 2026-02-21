using System;

namespace LawAfrica.API.DTOs.Lawyers
{
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
}