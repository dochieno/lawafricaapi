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

        [Required, MaxLength(30)]
        public string ReportNumber { get; set; } = string.Empty;

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

        // ✅ Alias for frontend field "postCode" (your Admin UI currently sends this)
        // ResolveTownAsync in controller will check TownPostCode first, then this alias.
        [MaxLength(20)]
        [JsonPropertyName("postCode")]
        public string? PostCode { get; set; }

        // ✅ Read-only; NEVER assign to it in controller
        public LegalDocumentCategory Category => LegalDocumentCategory.LLRServices;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (CountryId <= 0)
                yield return new ValidationResult("CountryId is required.", new[] { nameof(CountryId) });

            if (!ReportValidation.IsValidReportNumber(ReportNumber))
                yield return new ValidationResult(
                    "ReportNumber must start with 3 letters followed by digits, e.g. CAR353.",
                    new[] { nameof(ReportNumber) });

            if (string.IsNullOrWhiteSpace(ContentText))
                yield return new ValidationResult("ContentText is required.", new[] { nameof(ContentText) });

            if (CourtType <= 0)
                yield return new ValidationResult("CourtType is required.", new[] { nameof(CourtType) });

            // ✅ Optional: allow TownId OR TownPostCode OR Town text
            if (TownId.HasValue && TownId.Value <= 0)
                yield return new ValidationResult("TownId must be a positive number.", new[] { nameof(TownId) });

            var pc = (TownPostCode ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(pc) && pc.Length > 20)
                yield return new ValidationResult("TownPostCode is too long.", new[] { nameof(TownPostCode) });

            // ✅ Alias length check
            var pc2 = (PostCode ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(pc2) && pc2.Length > 20)
                yield return new ValidationResult("PostCode is too long.", new[] { nameof(PostCode) });

            // ✅ Optional: CourtId must be positive if provided
            if (CourtId.HasValue && CourtId.Value <= 0)
                yield return new ValidationResult("CourtId must be a positive number.", new[] { nameof(CourtId) });
        }
    }
}
