using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LawAfrica.API.Models.Ai.Commentary
{
    [Table("AiCommentaryThreads")]
    public class AiCommentaryThread
    {
        public long Id { get; set; }

        [Required]
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        /// <summary>
        /// Optional: If you later support institution-wide shared threads.
        /// </summary>
        public int? InstitutionId { get; set; }

        /// <summary>
        /// UI title (auto from first question; user-editable later).
        /// </summary>
        [MaxLength(200)]
        public string Title { get; set; } = "New conversation";

        /// <summary>
        /// The user's primary jurisdiction at the time thread was created.
        /// </summary>
        public int? CountryId { get; set; }

        [MaxLength(120)]
        public string? CountryName { get; set; }

        [MaxLength(20)]
        public string? CountryIso { get; set; }

        [MaxLength(60)]
        public string? RegionLabel { get; set; } // e.g. "East Africa"

        /// <summary>
        /// "basic" | "extended" — last applied mode in this thread
        /// </summary>
        [MaxLength(20)]
        public string Mode { get; set; } = "basic";

        /// <summary>
        /// Last model used (for debugging / analytics)
        /// </summary>
        [MaxLength(80)]
        public string? LastModel { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;

        // Soft delete (user removes it from their history)
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAtUtc { get; set; }
    }
}
