namespace LawAfrica.API.DTOs.Reports
{
    public enum ImportDuplicateStrategy
    {
        Skip = 1,
        Update = 2
    }

    public class ReportImportConfirmRequest
    {
        public ImportDuplicateStrategy DuplicateStrategy { get; set; } = ImportDuplicateStrategy.Skip;

        // items must be the "valid" preview items client accepted
        public List<LawReportUpsertDto> Items { get; set; } = new();
    }

    public class ReportImportConfirmResult
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
