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

        public string? Court { get; set; }     // optional legacy/display
        public int? CourtType { get; set; }

        public string? Town { get; set; }

        public string? Parties { get; set; }
        public string? Judges { get; set; }
        public DateTime? DecisionDate { get; set; }

        public string ContentText { get; set; } = "";

        public string Title { get; set; } = "";

        public int CountryId { get; set; }

        public ReportService Service { get; set; }

        // ✅ Always LLR Services
        public LegalDocumentCategory Category => LegalDocumentCategory.LLRServices;

        // ✅ Labels (NEW)
        public string? ServiceLabel { get; set; }
        public string? CourtTypeLabel { get; set; }
        public string? DecisionTypeLabel { get; set; }
        public string? CaseTypeLabel { get; set; }

        public int? TownId { get; set; }
        public string? TownPostCode { get; set; }

        // ================================
        // ✅ Entitlement / Gating meta (NEW)
        // ================================
        public bool IsBlocked { get; set; }             // true for hard blocks (seat/inactive)
        public string? BlockReason { get; set; }        // enum string (e.g. "InstitutionSubscriptionInactive")
        public string? BlockMessage { get; set; }       // friendly message for UI

        public bool HardStop { get; set; }              // true => UI must stop (subscribe/pay) before showing more
        public string AccessLevel { get; set; } = "Preview";  // "FullAccess" | "PreviewOnly" (string for UI simplicity)

        public int? RequiredProductId { get; set; }
        public string? RequiredProductName { get; set; }
        public string? RequiredAction { get; set; }     // "Subscribe" | "Buy" | "None"

        public string? CtaLabel { get; set; }
        public string? CtaUrl { get; set; }
        public string? SecondaryCtaLabel { get; set; }
        public string? SecondaryCtaUrl { get; set; }

        public int? PreviewMaxChars { get; set; }
        public int? PreviewMaxParagraphs { get; set; }

        public bool FromCache { get; set; }             // optional: keep for later analytics
        public string? GrantSource { get; set; }        // e.g. "PersonalSubscription"
        public string? DebugNote { get; set; }          // safe for dev; hide in prod UI
        public bool IsPremium { get; set; }
        public int? CourtId { get; set; }
        public string? CourtName { get; set; }
        public string? CourtCode { get; set; }





    }
}
