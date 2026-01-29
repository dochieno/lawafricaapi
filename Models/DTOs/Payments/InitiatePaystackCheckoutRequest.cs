using LawAfrica.API.Models.Payments;
using System.Text.Json.Serialization;

namespace LawAfrica.API.Models.DTOs.Payments
{
    public class InitiatePaystackCheckoutRequest
    {
        public PaymentPurpose Purpose { get; set; }

        public decimal Amount { get; set; }
        public string? Currency { get; set; } = "KES";

        // Who/what (same pattern as MPesa)
        public int? InstitutionId { get; set; }
        public int? RegistrationIntentId { get; set; }
        public int? ContentProductId { get; set; }
        public int? DurationInMonths { get; set; }
        public int? LegalDocumentId { get; set; }

        [JsonPropertyName("clientReturnUrl")]
        public string? ClientReturnUrl { get; set; }

        // Accept alias from mobile if sent as callbackUrl
        [JsonPropertyName("callbackUrl")]
        public string? CallbackUrl { get; set; }
        public string? Email { get; set; }


    }
}
