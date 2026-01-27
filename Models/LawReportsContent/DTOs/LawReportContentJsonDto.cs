using LawAfrica.API.Models.LawReportsContent.Models;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LawAfrica.API.Models.LawReportsContent.DTOs
{
    public class LawReportContentJsonDto
    {
        public int LawReportId { get; set; }
        public string? Hash { get; set; }
        public List<LawReportContentJsonBlockDto> Blocks { get; set; } = new();
    }

    public class LawReportContentJsonBlockDto
    {
        public int Order { get; set; }

        // 🔒 Internal enum (NOT serialized)
        [JsonIgnore]
        public LawReportContentBlockType Type { get; set; }

        // ✅ What the frontend consumes
        [JsonPropertyName("type")]
        public string TypeName => Type switch
        {
            LawReportContentBlockType.Title => "title",
            LawReportContentBlockType.MetaLine => "metaline",
            LawReportContentBlockType.Heading => "heading",
            LawReportContentBlockType.Paragraph => "paragraph",
            LawReportContentBlockType.ListItem => "listitem",
            LawReportContentBlockType.Quote => "quote",
            LawReportContentBlockType.Divider => "divider",
            LawReportContentBlockType.Spacer => "spacer",
            _ => "unknown"
        };

        public string? Text { get; set; }
        public object? Data { get; set; }      // structured payload (meta/list/etc.)
        public int? Indent { get; set; }
        public string? Style { get; set; }
    }
}