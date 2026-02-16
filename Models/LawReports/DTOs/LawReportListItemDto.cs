using LawAfrica.API.Models.LawReports.Enums;
using LawAfrica.API.Models.Reports;

namespace LawAfrica.API.Models.LawReports.DTOs
{
    public class LawReportListItemDto
    {
        public int Id { get; set; }

        public int LegalDocumentId { get; set; }

        public string Title { get; set; } = "";

        public bool IsPremium { get; set; }

        public string? ReportNumber { get; set; }

        public int Year { get; set; }

        public string? CaseNumber { get; set; }

        public string? Citation { get; set; }

        public int CourtType { get; set; }

        public string CourtTypeLabel { get; set; } = "";

        public ReportDecisionType DecisionType { get; set; }

        public string DecisionTypeLabel { get; set; } = "";

        public ReportCaseType CaseType { get; set; }

        public string CaseTypeLabel { get; set; } = "";

        public string? Court { get; set; }

        public string? Town { get; set; }

        public int? TownId { get; set; }

        public string? TownPostCode { get; set; }

        public string? Parties { get; set; }

        public string? Judges { get; set; }

        public DateTime? DecisionDate { get; set; }

        public string PreviewText { get; set; } = "";
    }
}
