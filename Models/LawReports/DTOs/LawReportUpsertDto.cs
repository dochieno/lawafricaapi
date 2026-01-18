using LawAfrica.API.Models;
using LawAfrica.API.Models.Reports;
using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Reports
{
    public class LawReportUpsertDto : IValidatableObject
    {
        // ✅ Required
        [Range(1, int.MaxValue)]
        public int CountryId { get; set; }

        // ✅ New required
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

        [MaxLength(200)]
        public string? Court { get; set; }

        [MaxLength(200)]
        public string? Parties { get; set; }

        [MaxLength(2000)]
        public string? Judges { get; set; }

        public DateTime? DecisionDate { get; set; }

        [Required]
        public string ContentText { get; set; } = string.Empty;
        public string? Town { get; set; }

        // ✅ Read-only is fine; just NEVER assign to it in controller
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
        }
    }
}
