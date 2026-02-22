using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Lawyers
{
    public class LawyerService
    {
        public int Id { get; set; }

        [Required, MaxLength(140)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(140)]
        public string? Slug { get; set; }

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}