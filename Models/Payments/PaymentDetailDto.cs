using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Models.DTOs.Payments
{
    public class PaymentDetailDto
    {
        public int PaymentIntentId { get; set; }

        public PaymentPurpose Purpose { get; set; }
        public PaymentMethod Method { get; set; }
        public PaymentStatus Status { get; set; }

        public decimal Amount { get; set; }
        public string Currency { get; set; } = "KES";

        public string? PhoneNumber { get; set; }
        public string? ProviderResultCode { get; set; }
        public string? ProviderResultDesc { get; set; }
        public string? ProviderReference { get; set; }

        public int? UserId { get; set; }
        public int? InstitutionId { get; set; }
        public int? ContentProductId { get; set; }

        public bool IsFinalized { get; set; }

        public string? AdminNotes { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTime? ApprovedAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? LegalDocumentId { get; set; } // ✅ NEW

    }
}
