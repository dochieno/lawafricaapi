using System.ComponentModel.DataAnnotations;
using LawAfrica.API.Models;

namespace LawAfrica.API.Models.Reports
{
    public enum ReportDecisionType
    {
        Judgment = 1,
        Ruling = 2
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

        // -------------------
        // DEDUPE / IDENTITY
        // -------------------
        [MaxLength(120)]
        public string? Citation { get; set; } // optional but preferred if available

        [Required, MaxLength(30)]
        public string ReportNumber { get; set; } = string.Empty; // e.g. CAR353

        public int Year { get; set; }

        [MaxLength(120)]
        public string? CaseNumber { get; set; } // e.g. Petition 12 of 2020

        // -------------------
        // CLASSIFICATION
        // -------------------
        public ReportDecisionType DecisionType { get; set; }
        public ReportCaseType CaseType { get; set; }

        // -------------------
        // METADATA
        // -------------------
        [MaxLength(200)]
        public string? Court { get; set; }

        [MaxLength(200)]
        public string? Parties { get; set; } // "A v B"

        [MaxLength(500)]
        public string? Judges { get; set; } // single text field, split in UI by newline/semicolon

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
    }
}
