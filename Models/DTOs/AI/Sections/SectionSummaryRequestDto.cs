using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.DTOs.Ai.Sections
{
    public class SectionSummaryRequestDto
    {
        // Optional: ToC entry id (preferred path)
        public int? TocEntryId { get; set; }

        [Required]
        public int LegalDocumentId { get; set; }

        // “basic” | “extended”
        [Required, MaxLength(20)]
        public string Type { get; set; } = "basic";

        // ✅ Optional now (only required when TocEntryId is NOT provided)
        // Physical PDF pages (1-based)
        [Range(1, int.MaxValue)]
        public int? StartPage { get; set; }

        [Range(1, int.MaxValue)]
        public int? EndPage { get; set; }

        public bool ForceRegenerate { get; set; } = false;

        [MaxLength(500)]
        public string? SectionTitle { get; set; }
    }
}
