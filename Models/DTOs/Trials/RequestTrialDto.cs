using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.DTOs.Trials
{
    public class RequestTrialDto
    {
        [Required]
        [Range(1, int.MaxValue)]
        public int ContentProductId { get; set; }

        [MaxLength(800)]
        public string? Reason { get; set; }
    }
}
