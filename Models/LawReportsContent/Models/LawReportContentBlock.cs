using System;

namespace LawAfrica.API.Models.LawReportsContent.Models
{
    public class LawReportContentBlock
    {
        public long Id { get; set; }

        public int LawReportId { get; set; }

        public int Order { get; set; }

        public LawReportContentBlockType Type { get; set; }

        // Simple text blocks (paragraph, heading, title)
        public string? Text { get; set; }

        // Structured blocks (meta lines, list items) stored as JSON string (jsonb in DB is fine)
        public string? Json { get; set; }

        public int? Indent { get; set; }
        public string? Style { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}