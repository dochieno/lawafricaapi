using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.DTOs.Trials
{
    public class ReviewTrialDto
    {
        [MaxLength(800)]
        public string? AdminNotes { get; set; }
    }
}
