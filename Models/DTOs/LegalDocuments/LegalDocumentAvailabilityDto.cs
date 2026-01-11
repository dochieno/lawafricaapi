namespace LawAfrica.API.Models.DTOs.LegalDocuments
{
    public class LegalDocumentAvailabilityDto
    {
        public int DocumentId { get; set; }
        public bool HasContent { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
