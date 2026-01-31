namespace LawAfrica.API.Models.DTOs.Ai.Sections
{
    public class SectionSummaryResponseDto
    {
        public int LegalDocumentId { get; set; }
        public int? TocEntryId { get; set; }

        public string Type { get; set; } = "basic";

        // ✅ What the client asked for (raw request)
        public int RequestedStartPage { get; set; }
        public int RequestedEndPage { get; set; }

        // ✅ What the backend actually used (safe + clamped)
        public int StartPage { get; set; }   // effective start (physical PDF pages)
        public int EndPage { get; set; }     // effective end   (physical PDF pages)

        // Summary payload
        public string Summary { get; set; } = string.Empty;

        // Metadata
        public bool FromCache { get; set; }
        public int InputCharCount { get; set; }

        // ✅ Safety + transparency
        public List<string> Warnings { get; set; } = new();

        public DateTime GeneratedAt { get; set; }
    }
}
