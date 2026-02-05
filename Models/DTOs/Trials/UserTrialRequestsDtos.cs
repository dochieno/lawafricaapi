using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Trials
{
    public class CreateTrialRequestDto
    {
        [Required]
        public int ContentProductId { get; set; }

        [MaxLength(800)]
        public string? Reason { get; set; }
    }

    public class ReviewTrialRequestDto
    {
        [Required]
        public int RequestId { get; set; }

        // true = approve, false = reject
        public bool Approve { get; set; }

        [MaxLength(800)]
        public string? AdminNotes { get; set; }

        // Optional: allow admin to override duration (default 7)
        [Range(1, 30)]
        public int? DurationDays { get; set; }
    }
}

