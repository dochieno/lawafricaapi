using LawAfrica.API.Models;
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

        public string Title { get; set; } = "";

        // ✅ Needed
        public int CountryId { get; set; }

        // ✅ New
        public ReportService Service { get; set; }

        // ✅ Always LLR Services
        public LegalDocumentCategory Category => LegalDocumentCategory.LLRServices;
        public string? Town { get; set; }

    }
}
