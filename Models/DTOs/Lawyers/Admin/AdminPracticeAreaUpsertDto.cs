using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Lawyers.Admin
{
    public class AdminPracticeAreaUpsertDto
    {
        [Required, MaxLength(160)]
        public string Name { get; set; } = "";

        [MaxLength(160)]
        public string? Slug { get; set; }

        public bool IsActive { get; set; } = true;
    }
}