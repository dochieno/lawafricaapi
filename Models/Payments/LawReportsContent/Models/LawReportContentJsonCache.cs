using System;

namespace LawAfrica.API.Models.Payments.LawReportsContent.Models
{
    // 1:1 keyed by LawReportId (see your EF config)
    public class LawReportContentJsonCache
    {
        public int LawReportId { get; set; }

        // Final normalized JSON for the reader (stored as jsonb)
        public string Json { get; set; } = "{}";

        // Hash of raw input used to build this JSON (detect stale cache)
        public string Hash { get; set; } = "";

        // e.g., "block-builder:v1"
        public string? BuiltBy { get; set; }

        public DateTime BuiltAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}