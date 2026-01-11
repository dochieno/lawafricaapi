using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models
{
    public class LegalDocumentNote
    {
        public int Id { get; set; }

        // ---------------- RELATIONSHIPS ----------------
        public int LegalDocumentId { get; set; }
        public LegalDocument LegalDocument { get; set; } = null!;

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        // ---------------- HIGHLIGHT ----------------
        // Exact text selected from the document (immutable)
        public string? HighlightedText { get; set; }

        // Position-based (future-proof)
        public int? PageNumber { get; set; }
        public int? CharOffsetStart { get; set; }
        public int? CharOffsetEnd { get; set; }

        // ---------------- NOTE CONTENT ----------------
        // User commentary (editable)
        [Required]
        public string Content { get; set; } = string.Empty;

        public string? Chapter { get; set; }

        // ---------------- AUDIT ----------------
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public string HighlightColor { get; set; } = "yellow";

    }
}
