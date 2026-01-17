using LawAfrica.API.Models.Reports;

namespace LawAfrica.API.DTOs.Reports
{
    public class LawReportDto
    {
        public int Id { get; set; }
        public int LegalDocumentId { get; set; }

        public string ReportNumber { get; set; } = "";
        public int Year { get; set; }
        public string? CaseNumber { get; set; }
        public string? Citation { get; set; }

        public ReportDecisionType DecisionType { get; set; }
        public ReportCaseType CaseType { get; set; }

        public string? Court { get; set; }
        public string? Parties { get; set; }
        public string? Judges { get; set; }
        public DateTime? DecisionDate { get; set; }

        public string ContentText { get; set; } = "";

        // Helpful for catalog UI
        public string Title { get; set; } = "";
    }
}
