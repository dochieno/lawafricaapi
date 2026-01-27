using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LawAfrica.API.Models.Ai
{
    public sealed class AiLawReportFormatResult
    {
        [JsonPropertyName("blocks")]
        public List<AiLawReportRangeBlock> Blocks { get; set; } = new();
    }

    public sealed class AiLawReportRangeBlock
    {
        // title | meta | heading | paragraph | list_item | divider | spacer
        [JsonPropertyName("type")]
        public string Type { get; set; } = "paragraph";

        [JsonPropertyName("start")]
        public int Start { get; set; }

        [JsonPropertyName("end")]
        public int End { get; set; }

        // list only
        [JsonPropertyName("marker")]
        public string? Marker { get; set; }

        [JsonPropertyName("indent")]
        public int? Indent { get; set; }
    }
}