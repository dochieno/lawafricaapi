using System;
using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models
{
    /// <summary>
    /// Who the price applies to.
    /// </summary>
    public enum PricingAudience : short
    {
        Public = 1,
        Institution = 2
    }

    /// <summary>
    /// Billing period for subscriptions (extensible later).
    /// </summary>
    public enum BillingPeriod : short
    {
        Monthly = 1,
        Annual = 2
    }

    /// <summary>
    /// Price plan for a ContentProduct, scoped by Audience + BillingPeriod + Currency.
    /// Supports effective dates + active flag for safe price changes.
    /// </summary>
    public class ContentProductPrice
    {
        public int Id { get; set; }

        public int ContentProductId { get; set; }
        public ContentProduct ContentProduct { get; set; } = null!;

        public PricingAudience Audience { get; set; } = PricingAudience.Public;

        public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;

        /// <summary>
        /// ISO currency code e.g. KES, USD.
        /// </summary>
        [MaxLength(10)]
        public string Currency { get; set; } = "KES";

        /// <summary>
        /// Price amount in MAJOR units (consistent with your Invoice/PaymentIntent decimal Amount).
        /// e.g. 1000.00 (KES)
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Whether the plan is selectable right now.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Optional effective window for pricing lifecycle.
        /// If null -> valid immediately/indefinitely.
        /// </summary>
        public DateTime? EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
