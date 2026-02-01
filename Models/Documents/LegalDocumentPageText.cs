using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LawAfrica.API.Models.Documents
{
    [Table("LegalDocumentPageTexts")]
    public class LegalDocumentPageText
    {
        public int Id { get; set; }

        [Required]
        public int LegalDocumentId { get; set; }
        public LegalDocument LegalDocument { get; set; } = null!;

        // Physical PDF page number (1-based)
        public int PageNumber { get; set; }

        // Extracted text for that page
        [Required]
        public string Text { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
