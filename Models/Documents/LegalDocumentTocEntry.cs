using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LawAfrica.API.Models.Documents
{
    public enum TocEntryLevel
    {
        Chapter = 1,
        Section = 2,
        Subsection = 3
    }

    public enum TocTargetType
    {
        PageRange = 1, // PDF-friendly
        Anchor = 2     // Reader-block/anchor-friendly
    }

    public class LegalDocumentTocEntry
    {
        public int Id { get; set; }

        // Parent document
        public int LegalDocumentId { get; set; }
        public LegalDocument LegalDocument { get; set; } = null!;

        // Hierarchy (self-referencing)
        public int? ParentId { get; set; }
        public LegalDocumentTocEntry? Parent { get; set; }
        public ICollection<LegalDocumentTocEntry> Children { get; set; } = new List<LegalDocumentTocEntry>();

        // Display
        [Required, MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        public TocEntryLevel Level { get; set; } = TocEntryLevel.Section;

        // Ordering within the same parent (or root if ParentId is null)
        public int Order { get; set; }

        // Destination type
        public TocTargetType TargetType { get; set; } = TocTargetType.PageRange;

        // Page destination (1-based pages, supports Roman numerals only as display text)
        public int? StartPage { get; set; }
        public int? EndPage { get; set; }

        // Anchor destination (for reader anchors / blocks)
        [MaxLength(200)]
        public string? AnchorId { get; set; }

        // Optional display label (e.g., "ix", "xi", "497", etc.)
        // This is purely UI/print friendly, not used for navigation logic.
        [MaxLength(50)]
        public string? PageLabel { get; set; }

        // Admin-only notes (not shown in reader)
        [MaxLength(2000)]
        public string? Notes { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
