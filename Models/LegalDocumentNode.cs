using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models
{
    public class LegalDocumentNode
    {
        public int Id { get; set; }

        // Parent document
        public int LegalDocumentId { get; set; }
        public LegalDocument LegalDocument { get; set; } = null!;

        // Hierarchy (self-referencing)
        public int? ParentId { get; set; }
        public LegalDocumentNode? Parent { get; set; }
        public ICollection<LegalDocumentNode> Children { get; set; } = new List<LegalDocumentNode>();

        // Content
        [Required]
        public string Title { get; set; } = string.Empty;

        public string? Content { get; set; }   // HTML / Markdown later

        // Ordering
        public int Order { get; set; }

        // Type: Chapter, Part, Section, Article
        [Required]
        public string NodeType { get; set; } = "Section";

        // Metadata
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
