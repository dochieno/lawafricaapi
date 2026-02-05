using System;
using System.Collections.Generic;
using LawAfrica.API.Models;

namespace LawAfrica.API.Models.DTOs.Subscriptions
{
    public sealed class SubscriptionProductsResponseDto
    {
        public DateTime NowUtc { get; set; }
        public PricingAudience Audience { get; set; }
        public List<SubscriptionProductDto> Products { get; set; } = new();
    }

    public sealed class SubscriptionProductDto
    {
        public int ContentProductId { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }

        public ProductAccessModel AccessModel { get; set; } // public/institution resolved
        public List<SubscriptionPlanDto> Plans { get; set; } = new();
    }

    public sealed class SubscriptionPlanDto
    {
        public int ContentProductPriceId { get; set; }
        public BillingPeriod BillingPeriod { get; set; }
        public string Currency { get; set; } = "KES";
        public decimal Amount { get; set; }

        public bool IsActive { get; set; }
        public DateTime? EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }
    }
}
