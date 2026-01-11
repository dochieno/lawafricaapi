namespace LawAfrica.API.Models.DTOs.Reader
{
    public class LegalDocumentReaderInfoResponse
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string? Author { get; set; }
        public string FileType { get; set; } = "pdf";
        public long FileSizeBytes { get; set; }
        public int? PageCount { get; set; }
        public int? ChapterCount { get; set; }
        public bool IsPremium { get; set; }
        public string? CoverUrl { get; set; }          // reader-friendly URL
        public string? DownloadUrl { get; set; }       // streaming URL
        public string? ReadUrl { get; set; }           // streaming URL (same, but semantic)
    }
}
