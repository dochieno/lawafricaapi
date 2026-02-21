using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Lawyers
{
    public class CreateInquiryRequestDto
    {
        public int? LawyerProfileId { get; set; }
        public int? PracticeAreaId { get; set; }
        public int? TownId { get; set; }

        [Required, MaxLength(2000)]
        public string ProblemSummary { get; set; } = string.Empty;

        [MaxLength(80)]
        public string? PreferredContactMethod { get; set; }
    }
}