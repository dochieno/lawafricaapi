namespace LawAfrica.API.DTOs.Ai
{
    public class AiRelatedCaseDto
    {
        public int? LawReportId { get; set; }
        public string Title { get; set; } = "";
        public string? Citation { get; set; }
        public int? Year { get; set; }
        public string? Court { get; set; }
        public string Jurisdiction { get; set; } = "Kenya";
        public string? Url { get; set; }
        public double? Confidence { get; set; }
        public string? Note { get; set; } // used for outside Kenya disclaimer
    }
}