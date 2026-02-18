using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.DTOs.Ai.Sections
{
    public class SectionSummaryRequestDto
    {
        public int? TocEntryId { get; set; }

        [Required]
        public int LegalDocumentId { get; set; }

        [Required, MaxLength(20)]
        public string Type { get; set; } = "basic"; // "basic" | "extended"

        [Range(1, int.MaxValue)]
        public int? StartPage { get; set; }

        [Range(1, int.MaxValue)]
        public int? EndPage { get; set; }

        public bool ForceRegenerate { get; set; } = false;

        [MaxLength(500)]
        public string? SectionTitle { get; set; }

        // ✅ allow backend to version prompts + invalidate cache safely
        [MaxLength(30)]
        public string? PromptVersion { get; set; } // e.g. "v1"
    }
}
