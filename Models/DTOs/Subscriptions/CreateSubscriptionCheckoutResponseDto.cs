using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Models.DTOs.Subscriptions
{
    public sealed class CreateSubscriptionCheckoutResponseDto
    {
        public int PaymentIntentId { get; set; }
        public PaymentProvider Provider { get; set; }
        public string Status { get; set; } = "Pending";

        public decimal Amount { get; set; }
        public string Currency { get; set; } = "KES";

        // Paystack only
        public string? AuthorizationUrl { get; set; }
        public string? Reference { get; set; }

        // Mpesa only
        public string? MerchantRequestId { get; set; }
        public string? CheckoutRequestId { get; set; }

        public string? Message { get; set; }
    }
}
