namespace LawAfrica.API.Models.DTOs.LegalDocuments
{
    public class LegalDocumentListItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsPremium { get; set; }
        public string CountryName { get; set; } = string.Empty;
        public LegalDocumentFileType FileType { get; set; }
        public bool AllowPublicPurchase { get; set; }
        public decimal? PublicPrice { get; set; }
        public string? PublicCurrency { get; set; }

    }
}
