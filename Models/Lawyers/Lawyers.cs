using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Lawyers
{
    public class PracticeArea
    {
        public int Id { get; set; }

        [Required, MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Slug { get; set; } // optional

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}