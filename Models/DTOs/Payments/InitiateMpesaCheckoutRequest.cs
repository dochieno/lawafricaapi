using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Models.DTOs.Payments
{
    /// <summary>
    /// Initiates a payment attempt. We create PaymentIntent and then call Mpesa STK push.
    /// </summary>
    public class InitiateMpesaCheckoutRequest
    {
        public PaymentPurpose Purpose { get; set; }

        public decimal Amount { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;

        // Purpose routing:
        public int? RegistrationIntentId { get; set; }    // for PublicSignupFee
        public int? ContentProductId { get; set; }        // for purchases/subscriptions
        public int? InstitutionId { get; set; }           // for InstitutionProductSubscription

        /// <summary>
        /// ✅ NEW: Selected pricing plan (recommended for subscriptions).
        /// Server uses this to compute amount/currency and duration (Monthly/Annual).
        /// </summary>
        public int? ContentProductPriceId { get; set; }

        /// <summary>
        /// Legacy: Used for subscriptions (months), optional.
        /// If ContentProductPriceId is provided, server ignores this for paid subscriptions.
        /// </summary>
        public int? DurationInMonths { get; set; }

        public int? LegalDocumentId { get; set; }  // ✅ for PublicLegalDocumentPurchase
    }
}
