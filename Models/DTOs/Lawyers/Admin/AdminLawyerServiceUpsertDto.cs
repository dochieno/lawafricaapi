using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Lawyers.Admin
{
    public class AdminLawyerServiceUpsertDto
    {
        [Required, MaxLength(140)]
        public string Name { get; set; } = "";

        [MaxLength(140)]
        public string? Slug { get; set; }

        public int SortOrder { get; set; } = 0;

        public bool IsActive { get; set; } = true;
    }
}