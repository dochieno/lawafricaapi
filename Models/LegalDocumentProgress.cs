using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models
{
    public class LegalDocumentProgress
    {
        public int Id { get; set; }

        // ---------------- RELATIONSHIPS ----------------
        [Required]
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        [Required]
        public int LegalDocumentId { get; set; }
        public LegalDocument LegalDocument { get; set; } = null!;

        // ---------------- POSITION ----------------
        public int? PageNumber { get; set; }      // PDF
        public string? Cfi { get; set; }          // EPUB
        public int? CharOffset { get; set; }      // HTML/Text (future)

        // ---------------- PROGRESS ----------------
        public double Percentage { get; set; }    // 0..100
        public bool IsCompleted { get; set; }

        // ---------------- METRICS ----------------
        public int TotalSecondsRead { get; set; }

        // ---------------- AUDIT ----------------
        public DateTime LastReadAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
