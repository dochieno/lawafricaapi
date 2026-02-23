using System.ComponentModel.DataAnnotations;
using LawAfrica.API.Models;
using LawAfrica.API.Models.LawReports.Enums;

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

        public int? SubCategoryId { get; set; }  // ✅ NEW

        [Required]
        public int CountryId { get; set; }

        // ✅ NEW: Kind selector (Standard=1, Report=2)
        public LegalDocumentKind Kind { get; set; } = LegalDocumentKind.Standard;

        // ✅ Optional: immediately map to a product so it appears under DOCS count
        public int? ContentProductId { get; set; }

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

        // ✅ Public purchase settings
        public bool AllowPublicPurchase { get; set; } = false;
        public decimal? PublicPrice { get; set; }
        public string? PublicCurrency { get; set; } = "KES";

        public int? VatRateId { get; set; }
        public bool IsTaxInclusive { get; set; }


    }
}
