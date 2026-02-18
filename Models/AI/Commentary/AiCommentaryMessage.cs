using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LawAfrica.API.Models.Ai.Commentary
{
    [Table("AiCommentaryMessages")]
    public class AiCommentaryMessage
    {
        public long Id { get; set; }

        [Required]
        public long ThreadId { get; set; }
        public AiCommentaryThread Thread { get; set; } = null!;

        /// <summary>
        /// "user" | "assistant" | "system"
        /// </summary>
        [Required, MaxLength(20)]
        public string Role { get; set; } = "user";

        /// <summary>
        /// Stored as markdown for assistant; plain text for user.
        /// </summary>
        [Required]
        public string Content { get; set; } = "";

        /// <summary>
        /// Mode used for this assistant response ("basic" | "extended")
        /// </summary>
        [MaxLength(20)]
        public string? Mode { get; set; }

        /// <summary>
        /// Model used for assistant responses (optional).
        /// </summary>
        [MaxLength(80)]
        public string? Model { get; set; }

        /// <summary>
        /// Track disclaimer version so we can prove what was shown at the time.
        /// </summary>
        [MaxLength(40)]
        public string? DisclaimerVersion { get; set; }

        /// <summary>
        /// Minimal usage stats; optional but valuable for billing/quota later.
        /// </summary>
        public int? InputChars { get; set; }
        public int? OutputChars { get; set; }

        /// <summary>
        /// Store the user prompt hash for dedupe if you want.
        /// </summary>
        [MaxLength(80)]
        public string? PromptHash { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Soft delete per-message (optional; normally you delete the whole thread)
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAtUtc { get; set; }
        // ✅ Add this
        [Required]
        public string ContentMarkdown { get; set; } = "";
    }
}
