using LawAfrica.API.Models.LawReports.Enums;
using LawAfrica.API.Models.Locations;
using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Reports
{
    public enum ReportDecisionType
    {
        Judgment = 1,
        Ruling = 2,
        Award = 3,
        AwardByConsent = 4,
        NoticeofMotion = 5,
        InterpretationofAwrd = 6,
        Order = 7,
        InterpretationofAmendedOrder = 8
    }

    public enum ReportCaseType
    {
        Criminal = 1,
        Civil = 2,
        Environmental = 3,
        Family = 4,
        Commercial = 5,
        Constitutional = 6
    }

    public class LawReport
    {
        public int Id { get; set; }

        // ✅ 1:1 link
        public int LegalDocumentId { get; set; }
        public LegalDocument LegalDocument { get; set; } = null!;

        // ✅ Required for mapping to LegalDocument
        public int CountryId { get; set; }

        // ✅ New
        public ReportService Service { get; set; } = ReportService.LawAfricaLawReports_LLR;

        // -------------------
        // DEDUPE / IDENTITY
        // -------------------
        [MaxLength(120)]
        public string? Citation { get; set; }

        [Required, MaxLength(30)]
        public string ReportNumber { get; set; } = string.Empty;

        public int Year { get; set; }

        [MaxLength(120)]
        public string? CaseNumber { get; set; }

        // -------------------
        // CLASSIFICATION
        // -------------------
        public ReportDecisionType DecisionType { get; set; }
        public ReportCaseType CaseType { get; set; }

        public CourtType CourtType { get; set; } = CourtType.HighCourt;

        // -------------------
        // METADATA
        // -------------------
        [MaxLength(200)]
        public string? Court { get; set; } // optional legacy/display string

        public string? Town { get; set; }

        [MaxLength(200)]
        public string? Parties { get; set; }

        [MaxLength(2000)]
        public string? Judges { get; set; }

        public DateTime? DecisionDate { get; set; }

        // -------------------
        // CONTENT
        // -------------------
        [Required]
        public string ContentText { get; set; } = string.Empty;

        // -------------------
        // AUDIT
        // -------------------
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // ✅ NEW (optional FK) — preferred way
        public int? TownId { get; set; }
        public Town? TownRef { get; set; }
    }
}
