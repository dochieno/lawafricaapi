namespace LawAfrica.API.Models.DTOs.LegalDocumentNotes
{
    public class LegalDocumentNoteResponse
    {
        public int Id { get; set; }
        public int LegalDocumentId { get; set; }
        public string Content { get; set; } = string.Empty;
        public int? PageNumber { get; set; }
        public string? SectionReference { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
