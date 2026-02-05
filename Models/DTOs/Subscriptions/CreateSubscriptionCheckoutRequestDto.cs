using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Models.DTOs.Subscriptions
{
    public sealed class CreateSubscriptionCheckoutRequestDto
    {
        public int ContentProductPriceId { get; set; }

        // Mpesa | Paystack | Manual (enum in your system)
        public PaymentProvider Provider { get; set; } = PaymentProvider.Mpesa;

        // Required for Mpesa
        public string? PhoneNumber { get; set; }

        // Optional: where Paystack return proxy should redirect back to
        public string? ClientReturnUrl { get; set; }
    }
}
