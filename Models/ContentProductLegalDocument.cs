namespace LawAfrica.API.Models
{
    /// <summary>
    /// Join table: maps which LegalDocuments belong to which ContentProducts.
    /// Supports many-to-many.
    /// </summary>
    public class ContentProductLegalDocument
    {
        public int Id { get; set; }

        public int ContentProductId { get; set; }
        public ContentProduct ContentProduct { get; set; } = null!;

        public int LegalDocumentId { get; set; }
        public LegalDocument LegalDocument { get; set; } = null!;

        /// <summary>
        /// Optional ordering inside a product.
        /// </summary>
        public int SortOrder { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
