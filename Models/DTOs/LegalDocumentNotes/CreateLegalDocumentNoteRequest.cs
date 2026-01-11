using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.DTOs.LegalDocumentNotes
{
    public class CreateLegalDocumentNoteRequest
    {
        [Required]
        public int LegalDocumentId { get; set; }

        // Highlight data (optional)
        public string? HighlightedText { get; set; }

        public int? PageNumber { get; set; }
        public int? CharOffsetStart { get; set; }
        public int? CharOffsetEnd { get; set; }

        // Note content (required)
        [Required]
        public string Content { get; set; } = string.Empty;

        // ✅ OPTIONAL
        public string? Chapter { get; set; }
        public string? HighlightColor { get; set; } // ✅ NEW (optional)
    }
}
