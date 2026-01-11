using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models
{
    public class DocumentTextIndex
    {
        public int Id { get; set; }

        [Required]
        public int LegalDocumentId { get; set; }
        public LegalDocument LegalDocument { get; set; } = null!;

        [Required]
        public int PageNumber { get; set; }

        [Required]
        public string Text { get; set; } = string.Empty;

        public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
    }
}
