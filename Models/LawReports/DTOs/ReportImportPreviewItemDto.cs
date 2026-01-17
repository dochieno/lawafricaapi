namespace LawAfrica.API.DTOs.Reports
{
    public class ReportImportPreviewItemDto
    {
        public int RowNumber { get; set; } // Excel row or 1 for Word
        public string ReportNumber { get; set; } = "";
        public int Year { get; set; }
        public string? CaseNumber { get; set; }
        public string? Citation { get; set; }

        public string? Parties { get; set; }
        public string? Court { get; set; }
        public string? Judges { get; set; }
        public string? DecisionType { get; set; } // raw from file
        public string? CaseType { get; set; } // raw from file
        public DateTime? DecisionDate { get; set; }

        public string ContentText { get; set; } = "";

        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();

        public bool IsDuplicate { get; set; }
        public int? ExistingLawReportId { get; set; }
        public int? ExistingLegalDocumentId { get; set; }
        public string? DuplicateReason { get; set; }
    }

    public class ReportImportPreviewDto
    {
        public int Total { get; set; }
        public int Valid { get; set; }
        public int Invalid { get; set; }
        public int Duplicates { get; set; }
        public List<ReportImportPreviewItemDto> Items { get; set; } = new();
    }
}
