using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.LawReports.DTOs
{
    public class CourtUpsertDto
    {
        [Required]
        public int CountryId { get; set; }

        // optional input; usually blank => auto-generate (KE-CO-001)
        public string? Code { get; set; }

        [Required, MaxLength(160)]
        public string Name { get; set; } = "";

        // Allowed only: Criminal/Civil/Environmental/Labour
        [Required, MaxLength(40)]
        public string Category { get; set; } = "Civil";

        [MaxLength(40)]
        public string? Abbreviation { get; set; }

        public int? Level { get; set; }

        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; } = 0;

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}
