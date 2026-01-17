using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Reports
{
    public class LawReportContentDto
    {
        public int LawReportId { get; set; }
        public int LegalDocumentId { get; set; }
        public string Title { get; set; } = "";
        public string ContentText { get; set; } = "";
        public DateTime? UpdatedAt { get; set; }
    }

    public class LawReportContentUpsertDto
    {
        [Required]
        public string ContentText { get; set; } = "";
    }
}
