using System.ComponentModel.DataAnnotations;
using LawAfrica.API.Models.Lawyers;

namespace LawAfrica.API.DTOs.Lawyers
{
    public class LawyerServiceOfferingUpsertDto
    {
        [Range(1, int.MaxValue)]
        public int LawyerServiceId { get; set; }

        [MaxLength(10)]
        public string? Currency { get; set; } = "KES";

        public decimal? MinFee { get; set; }
        public decimal? MaxFee { get; set; }

        public LawyerRateUnit Unit { get; set; } = LawyerRateUnit.Negotiable;

        [MaxLength(800)]
        public string? Notes { get; set; }
    }
}