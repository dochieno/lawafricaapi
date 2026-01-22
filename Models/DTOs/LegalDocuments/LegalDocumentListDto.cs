using LawAfrica.API.Models.LawReports.Enums;

namespace LawAfrica.API.Models.DTOs.LegalDocuments
{
    public class LegalDocumentListDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }

        public string? Author { get; set; }
        public string? Publisher { get; set; }
        public string? Edition { get; set; }

        public string Category { get; set; } = string.Empty;

        public int CountryId { get; set; }
        public string CountryName { get; set; } = string.Empty;

        public string FileType { get; set; } = "pdf";
        public int? PageCount { get; set; }
        public int? ChapterCount { get; set; }

        public bool IsPremium { get; set; }

        public string Version { get; set; } = "1.0";
        public string Status { get; set; } = string.Empty;

        public DateTime? PublishedAt { get; set; }

        public string? CoverImagePath { get; set; }

        public LegalDocumentKind Kind { get; set; }

        // ✅ NEW
        public bool AllowPublicPurchase { get; set; }
        public decimal? PublicPrice { get; set; }
        public string? PublicCurrency { get; set; }
        public int? VatRateId { get; set; }
        public bool IsTaxInclusive { get; set; }


    }
}
