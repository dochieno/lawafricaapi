using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using LawAfrica.API.Models.Locations;

namespace LawAfrica.API.Models.LawReports.Models
{
    [Index(nameof(CountryId), nameof(Code), IsUnique = true)]
    [Index(nameof(CountryId), nameof(Name))]
    public class Court
    {
        public int Id { get; set; }

        // FK
        public int CountryId { get; set; }
        public Country Country { get; set; } = null!;

        // Auto-generated: KE-CO-001 (unique per CountryId)
        [Required, MaxLength(20)]
        public string Code { get; set; } = string.Empty;

        [Required, MaxLength(160)]
        public string Name { get; set; } = string.Empty; // e.g. High Court

        // Allowed: Criminal/Civil/Environmental/Labour
        [Required, MaxLength(40)]
        public string Category { get; set; } = "Civil";

        [MaxLength(40)]
        public string? Abbreviation { get; set; } // e.g. HC, CA

        public int? Level { get; set; } // optional sorting/hierarchy (1=SC, 2=CA, 3=HC...)

        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; } = 0;

        [MaxLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
