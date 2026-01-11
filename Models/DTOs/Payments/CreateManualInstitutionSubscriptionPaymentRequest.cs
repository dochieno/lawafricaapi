namespace LawAfrica.API.Models.DTOs.Payments
{
    /// <summary>
    /// Used when an institution pays via bank transfer and admin needs to record it.
    /// This creates a PaymentIntent in PendingApproval state.
    /// </summary>
    public class CreateManualInstitutionSubscriptionPaymentRequest
    {
        public int InstitutionId { get; set; }
        public int ContentProductId { get; set; }
        public int DurationInMonths { get; set; }

        public decimal Amount { get; set; }
        public string Currency { get; set; } = "KES";

        /// <summary>
        /// Bank/EFT reference number or receipt reference provided by institution.
        /// </summary>
        public string ManualReference { get; set; } = string.Empty;

        public string? AdminNotes { get; set; }
    }
}
