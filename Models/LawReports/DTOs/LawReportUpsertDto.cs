using LawAfrica.API.Models;
using LawAfrica.API.Models.Reports;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LawAfrica.API.DTOs.Reports
{
    public class LawReportUpsertDto : IValidatableObject
    {
        // ✅ Required
        [Range(1, int.MaxValue)]
        public int CountryId { get; set; }

        // ✅ Required enum
        [Required]
        public ReportService Service { get; set; } = ReportService.LawAfricaLawReports_LLR;

        // ✅ INTERNAL reference (server generated on Create)
        // Format: {CourtAbbrev}{CountryIso2}{00001} e.g. HCKE00001
        // Keep it optional in payload; controller will generate.
        [MaxLength(20)]
        public string? ReportNumber { get; set; }

        [Range(1900, 2100)]
        public int Year { get; set; }

        [MaxLength(120)]
        public string? CaseNumber { get; set; }

        [MaxLength(120)]
        public string? Citation { get; set; }

        [Required]
        public ReportDecisionType DecisionType { get; set; }

        [Required]
        public ReportCaseType CaseType { get; set; }

        // ✅ NEW (optional FK) — preferred way going forward
        [Range(1, int.MaxValue)]
        [JsonPropertyName("courtId")]
        public int? CourtId { get; set; }

        // Optional legacy/display
        [MaxLength(200)]
        public string? Court { get; set; }

        public int CourtType { get; set; } // 1..10

        public string? Town { get; set; }

        [MaxLength(200)]
        public string? Parties { get; set; }

        [MaxLength(2000)]
        public string? Judges { get; set; }

        public DateTime? DecisionDate { get; set; }

        [Required]
        public string ContentText { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int? TownId { get; set; }

        [MaxLength(20)]
        public string? TownPostCode { get; set; }

        // ✅ Optional category/division label (nullable)
        [MaxLength(120)]
        public string? CourtCategory { get; set; }

        // ✅ Alias for frontend field "postCode"
        [MaxLength(20)]
        [JsonPropertyName("postCode")]
        public string? PostCode { get; set; }

        // ✅ Read-only; NEVER assign to it in controller
        public LegalDocumentCategory Category => LegalDocumentCategory.LLRServices;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (CountryId <= 0)
                yield return new ValidationResult("CountryId is required.", new[] { nameof(CountryId) });

            if (string.IsNullOrWhiteSpace(ContentText))
                yield return new ValidationResult("ContentText is required.", new[] { nameof(ContentText) });

            if (CourtType <= 0)
                yield return new ValidationResult("CourtType is required.", new[] { nameof(CourtType) });

            if (TownId.HasValue && TownId.Value <= 0)
                yield return new ValidationResult("TownId must be a positive number.", new[] { nameof(TownId) });

            var pc = (TownPostCode ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(pc) && pc.Length > 20)
                yield return new ValidationResult("TownPostCode is too long.", new[] { nameof(TownPostCode) });

            var pc2 = (PostCode ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(pc2) && pc2.Length > 20)
                yield return new ValidationResult("PostCode is too long.", new[] { nameof(PostCode) });

            if (CourtId.HasValue && CourtId.Value <= 0)
                yield return new ValidationResult("CourtId must be a positive number.", new[] { nameof(CourtId) });

            // ✅ If client sends ReportNumber, keep it within limit (but it is server-owned)
            var rn = (ReportNumber ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(rn) && rn.Length > 12)
                yield return new ValidationResult("ReportNumber is too long.", new[] { nameof(ReportNumber) });
        }
    }
}
