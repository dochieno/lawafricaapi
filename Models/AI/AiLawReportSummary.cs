using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Ai
{
    public enum AiSummaryType
    {
        Basic = 1,
        Extended = 2
    }

    public class AiLawReportSummary
    {
        public int Id { get; set; }

        [Required]
        public int LawReportId { get; set; }

        [Required, MaxLength(64)]
        public string UserId { get; set; } = ""; // from JWT

        [Required]
        public AiSummaryType SummaryType { get; set; } = AiSummaryType.Basic;

        [Required]
        public string SummaryText { get; set; } = "";

        public int InputChars { get; set; }
        public int OutputChars { get; set; }

        [MaxLength(64)]
        public string Model { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}