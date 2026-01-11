using System.ComponentModel.DataAnnotations;
using LawAfrica.API.Models;

namespace LawAfrica.API.Models.DTOs
{
    public class CreateLegalDocumentRequest
    {
        [Required]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Author { get; set; }
        public string? Publisher { get; set; }
        public string? Edition { get; set; }

        [Required]
        public LegalDocumentCategory Category { get; set; }

        [Required]
        public int CountryId { get; set; }

        // ✅ Upload happens later via /upload, so do NOT require these at create time
        public string? FilePath { get; set; }
        public string? FileType { get; set; } = "pdf";

        public long FileSizeBytes { get; set; }
        public int? PageCount { get; set; }
        public int? ChapterCount { get; set; }

        public bool IsPremium { get; set; }
        public string Version { get; set; } = "1.0";

        public LegalDocumentStatus Status { get; set; } = LegalDocumentStatus.Draft;
        public DateTime? PublishedAt { get; set; }

        // ✅ NEW: Public purchase settings
        public bool AllowPublicPurchase { get; set; } = false;
        public decimal? PublicPrice { get; set; }
        public string? PublicCurrency { get; set; } = "KES";
    }
}
