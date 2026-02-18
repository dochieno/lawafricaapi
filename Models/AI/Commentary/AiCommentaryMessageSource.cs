using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LawAfrica.API.Models.Ai.Commentary
{
    [Table("AiCommentaryMessageSources")]
    public class AiCommentaryMessageSource
    {
        public long Id { get; set; }

        [Required]
        public long MessageId { get; set; }
        public AiCommentaryMessage Message { get; set; } = null!;

        /// <summary>
        /// "law_report" | "pdf_page" | "document" | "external"
        /// </summary>
        [Required, MaxLength(30)]
        public string Type { get; set; } = "document";

        public int? LawReportId { get; set; }
        public int? LegalDocumentId { get; set; }
        public int? PageNumber { get; set; }

        [MaxLength(300)]
        public string Title { get; set; } = "";

        [MaxLength(200)]
        public string Citation { get; set; } = "";

        /// <summary>
        /// The excerpt used at the time of answering (audit + user trust).
        /// Keep short.
        /// </summary>
        [MaxLength(1200)]
        public string Snippet { get; set; } = "";

        public double Score { get; set; }

        /// <summary>
        /// Optional: store a link your frontend can open directly.
        /// Relative URL recommended.
        /// </summary>
        [MaxLength(400)]
        public string? LinkUrl { get; set; }
    }
}
