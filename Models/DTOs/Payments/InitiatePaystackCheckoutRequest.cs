using LawAfrica.API.Models.Payments;

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

        /// <summary>
        /// Paystack requires customer email for initialize.
        /// If user is authenticated, backend will use the user's email and ignore this.
        /// If anonymous (e.g. public signup), this is required.
        /// </summary>
        public string? Email { get; set; }
    }
}
