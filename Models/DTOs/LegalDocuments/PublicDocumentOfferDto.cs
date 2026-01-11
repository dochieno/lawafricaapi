namespace LawAfrica.API.Models.DTOs.LegalDocuments
{
    public class PublicDocumentOfferDto
    {
        public int LegalDocumentId { get; set; }
        public bool AllowPublicPurchase { get; set; }
        public decimal? Price { get; set; }
        public string? Currency { get; set; }

        public bool AlreadyOwned { get; set; }
        public string Message { get; set; } = "";
    }
}
