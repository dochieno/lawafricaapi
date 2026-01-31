namespace LawAfrica.API.Models.DTOs.Ai.Sections
{
    public class SectionSummaryResponseDto
    {
        public int LegalDocumentId { get; set; }
        public int? TocEntryId { get; set; }

        public string Type { get; set; } = "basic";

        // Echo back pages used (physical PDF pages)
        public int StartPage { get; set; }
        public int EndPage { get; set; }

        // Summary payload
        public string Summary { get; set; } = string.Empty;

        // Metadata
        public bool FromCache { get; set; }
        public int InputCharCount { get; set; }

        public DateTime GeneratedAt { get; set; }
    }
}
