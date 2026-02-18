using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LawAfrica.API.Models.Ai.Sections
{
    [Table("AiLegalDocumentSectionSummaries")]
    public class AiLegalDocumentSectionSummary
    {
        public int Id { get; set; }

        // Who requested it (audit). Cache lookup SHOULD NOT depend on UserId.
        public int CreatedByUserId { get; set; }

        public int LegalDocumentId { get; set; }
        public int? TocEntryId { get; set; }

        // Range summarized (PDF pages)
        public int StartPage { get; set; }
        public int EndPage { get; set; }

        [Required, MaxLength(30)]
        public string Type { get; set; } = "basic";

        [Required, MaxLength(30)]
        public string PromptVersion { get; set; } = "v1";

        // ✅ new: section identity + cache integrity
        [Required, MaxLength(120)]
        public string OwnerKey { get; set; } = "";

        [Required, MaxLength(64)]
        public string ContentHash { get; set; } = ""; // SHA256 hex (64 chars)

        [Required, MaxLength(240)]
        public string CacheKey { get; set; } = ""; // doc+section+type+pv+hash

        [MaxLength(500)]
        public string? SectionTitle { get; set; }

        // Output
        [Required]
        public string Summary { get; set; } = string.Empty;

        public int? InputCharCount { get; set; }
        public int? TokensIn { get; set; }
        public int? TokensOut { get; set; }

        [MaxLength(80)]
        public string? ModelUsed { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
