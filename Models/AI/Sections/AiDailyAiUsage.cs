using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LawAfrica.API.Models.Ai.Sections
{
    [Table("AiDailyAiUsage")]
    public class AiDailyAiUsage
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        // UTC date bucket
        public DateTime DayUtc { get; set; } = DateTime.UtcNow.Date;

        // Requests made today
        public int Requests { get; set; }

        [Required, MaxLength(60)]
        public string Feature { get; set; } = "legal_doc_section_summary"; // keep it explicit

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
