using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Tax
{
    public class VatRate
    {
        public int Id { get; set; }

        [Required, MaxLength(32)]
        public string Code { get; set; } = "";

        [Required, MaxLength(160)]
        public string Name { get; set; } = "";

        // Percent e.g. 16.0000
        public decimal RatePercent { get; set; }

        // Optional: KE, UG, * or null (free-form)
        [MaxLength(8)]
        public string? CountryScope { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
