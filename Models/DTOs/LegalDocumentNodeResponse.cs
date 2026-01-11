namespace LawAfrica.API.Models.DTOs
{
    public class LegalDocumentNodeResponse
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string NodeType { get; set; } = string.Empty;
        public int Order { get; set; }
        public string? Content { get; set; }
        public List<LegalDocumentNodeResponse> Children { get; set; } = new();
    }
}
