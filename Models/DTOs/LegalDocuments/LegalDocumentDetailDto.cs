using LawAfrica.API.Models.DTOs.LegalDocuments;

public class LegalDocumentDetailDto : LegalDocumentListDto
{
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
