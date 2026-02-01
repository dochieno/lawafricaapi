namespace LawAfrica.API.Models.DTOs.Ai.Sections
{
    public class SectionSummaryResponseDto
    {
        public int LegalDocumentId { get; set; }
        public int? TocEntryId { get; set; }

        public string Type { get; set; } = "basic";

        // ✅ What the client asked for (raw request)
        // If TocEntryId-only, these can be 0 (or you can copy effective pages here)
        public int RequestedStartPage { get; set; }
        public int RequestedEndPage { get; set; }

        // ✅ What the backend actually used
        public int StartPage { get; set; }
        public int EndPage { get; set; }

        public string Summary { get; set; } = string.Empty;

        public bool FromCache { get; set; }
        public int InputCharCount { get; set; }

        public List<string> Warnings { get; set; } = new();

        public DateTime GeneratedAt { get; set; }
    }
}
