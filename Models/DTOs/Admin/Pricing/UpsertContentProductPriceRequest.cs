using System.ComponentModel.DataAnnotations;
using LawAfrica.API.Models;

namespace LawAfrica.API.DTOs.Admin.Pricing
{
    public class UpsertContentProductPriceRequest
    {
        [Required]
        public PricingAudience Audience { get; set; } = PricingAudience.Public;

        [Required]
        public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;

        [MaxLength(10)]
        public string Currency { get; set; } = "KES";

        [Range(0.01, 999999999)]
        public decimal Amount { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }
    }
}
