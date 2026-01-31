using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LawAfrica.API.Models.Ai.Sections
{
    [Table("AiLegalDocumentSectionSummaries")]
    public class AiLegalDocumentSectionSummary
    {
        public int Id { get; set; }

        // Who requested it
        public int UserId { get; set; }

        // What it is about
        public int LegalDocumentId { get; set; }

        // Optional: summary may be page-range-based only
        public int? TocEntryId { get; set; }

        // Range we summarized (physical PDF pages)
        public int StartPage { get; set; }
        public int EndPage { get; set; }

        // "basic" | "extended"
        [Required, MaxLength(30)]
        public string Type { get; set; } = "basic";

        // Cache key versioning
        [Required, MaxLength(30)]
        public string PromptVersion { get; set; } = "v1";

        // Output
        [Required]
        public string Summary { get; set; } = string.Empty;

        // Optional metadata
        public int? TokensIn { get; set; }
        public int? TokensOut { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
