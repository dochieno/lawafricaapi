using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Tax
{
    public class VatRule
    {
        public int Id { get; set; }

        [Required, MaxLength(64)]
        public string Purpose { get; set; } = "RegistrationFee"; // string for flexibility

        // e.g. "KE" or "*" or null
        [MaxLength(8)]
        public string? CountryCode { get; set; }

        public int VatRateId { get; set; }
        public VatRate? VatRate { get; set; }

        public int Priority { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
