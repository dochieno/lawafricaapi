using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.DTOs
{
    public class UpdateLegalDocumentNoteRequest
    {
        [Required]
        public string Content { get; set; } = string.Empty;

        public int? PageNumber { get; set; }
        public string? Chapter { get; set; } // optional

        public string? HighlightColor { get; set; } // ✅ NEW
    }
}
