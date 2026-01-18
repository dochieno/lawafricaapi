using System.ComponentModel.DataAnnotations;
using LawAfrica.API.Models;

namespace LawAfrica.API.Models.Locations
{
    public class Town
    {
        public int Id { get; set; }

        [Range(1, int.MaxValue)]
        public int CountryId { get; set; }
        public Country Country { get; set; } = null!;

        // Post code as text (some countries have letters, spaces, etc.)
        [Required, MaxLength(20)]
        public string PostCode { get; set; } = string.Empty;

        // "Town" name
        [Required, MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
