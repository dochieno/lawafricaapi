namespace LawAfrica.API.Models.DTOs.Products
{
    public class AddDocumentToProductRequest
    {
        public int LegalDocumentId { get; set; }
        public int SortOrder { get; set; } = 0;
    }

    public class UpdateProductDocumentRequest
    {
        public int SortOrder { get; set; }
    }

    public class ProductDocumentDto
    {
        public int Id { get; set; }
        public int ContentProductId { get; set; }
        public int LegalDocumentId { get; set; }
        public int SortOrder { get; set; }

        public string DocumentTitle { get; set; } = string.Empty;
        public bool IsPremium { get; set; }
        public string Status { get; set; } = "—";

        public DateTime CreatedAt { get; set; }
    }
}
