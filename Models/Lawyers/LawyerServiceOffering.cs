using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Lawyers
{
    public class LawyerServiceOffering
    {
        public int LawyerProfileId { get; set; }
        public LawyerProfile LawyerProfile { get; set; } = null!;

        public int LawyerServiceId { get; set; }
        public LawyerService LawyerService { get; set; } = null!;

        [MaxLength(10)]
        public string Currency { get; set; } = "KES";

        // Numeric in DB; optional
        public decimal? MinFee { get; set; }
        public decimal? MaxFee { get; set; }

        public LawyerRateUnit Unit { get; set; } = LawyerRateUnit.Negotiable;

        [MaxLength(800)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}