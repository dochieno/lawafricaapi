using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models
{
    public class LegalDocumentAnnotation
    {
        public int Id { get; set; }

        // ---------------- RELATIONSHIPS ----------------
        [Required]
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        [Required]
        public int LegalDocumentId { get; set; }
        public LegalDocument LegalDocument { get; set; } = null!;

        // ---------------- TYPE ----------------
        // "highlight" | "note"
        [Required]
        public string Type { get; set; } = "highlight";

        // ---------------- LOCATION ----------------
        public int? PageNumber { get; set; }     // PDF
        public string? Cfi { get; set; }         // EPUB (future)
        public int? StartCharOffset { get; set; }
        public int? EndCharOffset { get; set; }

        // ---------------- CONTENT ----------------
        public string? SelectedText { get; set; } // highlighted text
        public string? Note { get; set; }         // user comment
        public string? Color { get; set; }        // e.g. yellow, blue
        // ---------------- AUDIT ----------------
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public Guid ClientId { get; set; }
    }
}
