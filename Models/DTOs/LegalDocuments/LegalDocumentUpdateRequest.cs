using LawAfrica.API.Models;

namespace LawAfrica.API.Models.DTOs
{
    public class LegalDocumentUpdateRequest
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }

        public string? Author { get; set; }
        public string? Publisher { get; set; }
        public string? Edition { get; set; }

        public LegalDocumentCategory Category { get; set; }

        public int CountryId { get; set; }

        public bool IsPremium { get; set; }
        public string Version { get; set; } = "1.0";

        public LegalDocumentStatus Status { get; set; }
        public DateTime? PublishedAt { get; set; }

        // ✅ NEW: Public purchase settings
        public bool AllowPublicPurchase { get; set; } = false;
        public decimal? PublicPrice { get; set; }
        public string? PublicCurrency { get; set; } = "KES";
    }
}
