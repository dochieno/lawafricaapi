using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Ai
{
    public class AiUsage
    {
        public int Id { get; set; }

        [Required, MaxLength(64)]
        public string UserId { get; set; } = "";

        // e.g. "2026-01" for monthly usage
        [Required, MaxLength(10)]
        public string PeriodKey { get; set; } = "";

        public int SummariesGenerated { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}