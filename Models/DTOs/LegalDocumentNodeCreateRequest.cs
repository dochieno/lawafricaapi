namespace LawAfrica.API.Models.DTOs
{
    public class LegalDocumentNodeCreateRequest
    {
        public int LegalDocumentId { get; set; }
        public int? ParentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Content { get; set; }
        public string NodeType { get; set; } = "Section";
        public int Order { get; set; }
    }
}
