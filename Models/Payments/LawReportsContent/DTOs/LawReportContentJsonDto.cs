using LawAfrica.API.Models.Payments.LawReportsContent.Models;
using System.Collections.Generic;

namespace LawAfrica.API.Models.LawReportsContent
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
        public LawReportContentBlockType Type { get; set; }
        public string? Text { get; set; }
        public object? Data { get; set; }      // structured payload (meta/list/etc.)
        public int? Indent { get; set; }
        public string? Style { get; set; }
    }
}