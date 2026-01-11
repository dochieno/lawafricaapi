using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Models.DTOs.Payments
{
    public class PaymentHistoryItemDto
    {
        public int PaymentIntentId { get; set; }

        public PaymentPurpose Purpose { get; set; }
        public PaymentMethod Method { get; set; }
        public PaymentStatus Status { get; set; }

        public decimal Amount { get; set; }
        public string Currency { get; set; } = "KES";

        public string? ProviderReference { get; set; } // MpesaReceipt or ManualRef
        public DateTime CreatedAt { get; set; }

        // Contextual info
        public int? ContentProductId { get; set; }
        public int? InstitutionId { get; set; }

        // Audit
        public bool IsFinalized { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public int? LegalDocumentId { get; set; } // ✅ NEW

    }
}
