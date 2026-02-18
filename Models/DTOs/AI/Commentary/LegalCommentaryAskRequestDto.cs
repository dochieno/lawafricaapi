using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.AI.Commentary
{
    public class LegalCommentaryAskRequestDto
    {
        /// <summary>
        /// Optional: conversation thread. If null/0, backend may create a new thread.
        /// Client should send it on subsequent messages to continue the same thread.
        /// </summary>
        public long? ThreadId { get; set; }

        [Required, MinLength(3), MaxLength(2500)]
        public string Question { get; set; } = "";

        /// <summary>
        /// "basic" | "extended" (backend will enforce based on subscription/tier)
        /// </summary>
        public string? Mode { get; set; }

        public string? JurisdictionHint { get; set; }

        public bool AllowExternalContext { get; set; } = true;

        public List<LegalCommentaryTurnDto>? History { get; set; }
    }

    public class LegalCommentaryTurnDto
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = "";
    }
}
