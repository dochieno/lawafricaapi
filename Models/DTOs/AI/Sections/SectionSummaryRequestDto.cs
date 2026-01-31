using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.DTOs.Ai.Sections
{
    public class SectionSummaryRequestDto
    {
        // Optional: ToC entry id (if you want to store/cache by toc entry later)
        public int? TocEntryId { get; set; }

        [Required]
        public int LegalDocumentId { get; set; }

        // “basic” | “extended”
        [Required, MaxLength(20)]
        public string Type { get; set; } = "basic";

        // Page range in *physical PDF pages* (1-based)
        [Range(1, int.MaxValue)]
        public int StartPage { get; set; }

        [Range(1, int.MaxValue)]
        public int EndPage { get; set; }

        // If true, bypass cache (later)
        public bool ForceRegenerate { get; set; } = false;

        // Optional: allow passing small extra context (title, etc.)
        [MaxLength(500)]
        public string? SectionTitle { get; set; }
    }
}
