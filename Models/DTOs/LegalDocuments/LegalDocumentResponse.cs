namespace LawAfrica.API.Models.DTOs
{
    public class LegalDocumentResponse
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }

        public string? Author { get; set; }
        public string? Publisher { get; set; }
        public string? Edition { get; set; }

        public string Category { get; set; } = string.Empty;
        public int CountryId { get; set; }
        public string CountryName { get; set; } = string.Empty;

        public string FileType { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public int? PageCount { get; set; }
        public int? ChapterCount { get; set; }

        public bool IsPremium { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public DateTime? PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public string? CoverImagePath { get; set; }
    }
}
