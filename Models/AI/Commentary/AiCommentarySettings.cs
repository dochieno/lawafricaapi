using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LawAfrica.API.Models.Ai.Commentary
{
    [Table("AiCommentarySettings")]
    public class AiCommentarySettings
    {
        public int Id { get; set; } = 1; // singleton row

        /// <summary>
        /// How many months to retain commentary threads before purge.
        /// Admin can change this. Not hard-coded.
        /// </summary>
        [Range(1, 60)]
        public int RetentionMonths { get; set; } = 6;

        /// <summary>
        /// If true, users can see conversation history in UI.
        /// </summary>
        public bool EnableUserHistory { get; set; } = true;

        /// <summary>
        /// Track changes (optional).
        /// </summary>
        public DateTime UpdatedAtUtc { get; set; }

        public int? UpdatedByUserId { get; set; }
        public User? UpdatedByUser { get; set; }
    }
}
