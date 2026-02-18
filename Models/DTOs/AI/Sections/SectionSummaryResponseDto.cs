namespace LawAfrica.API.Models.DTOs.Ai.Sections
{
    public class SectionSummaryResponseDto
    {
        public int LegalDocumentId { get; set; }
        public int? TocEntryId { get; set; }

        public string Type { get; set; } = "basic";

        // ✅ What the client asked for (raw request)
        public int? RequestedStartPage { get; set; }
        public int? RequestedEndPage { get; set; }

        // ✅ What the backend actually used (effective)
        public int StartPage { get; set; }
        public int EndPage { get; set; }

        // ✅ Echo back for UI clarity
        public string? SectionTitle { get; set; }

        // ✅ Critical: lets frontend verify this summary belongs to the selected section
        // Format: "doc:{id}|toc:{tocId}" OR "doc:{id}|range:{start}-{end}"
        public string OwnerKey { get; set; } = "";

        // ✅ Critical: cache correctness (content-owned)
        public string ContentHash { get; set; } = ""; // SHA256 of extracted text (normalized)
        public string CacheKey { get; set; } = "";    // full cache key used in DB

        public string Summary { get; set; } = string.Empty;

        public bool FromCache { get; set; }
        public int InputCharCount { get; set; }

        public List<string> Warnings { get; set; } = new();

        // ✅ audit / debugging
        public string? ModelUsed { get; set; }
        public string PromptVersion { get; set; } = "v1";

        public DateTime GeneratedAt { get; set; }
    }
}
