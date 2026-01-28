using System.ComponentModel.DataAnnotations.Schema;

namespace LawAfrica.API.Models.Payments
{
    /// <summary>
    /// Tracks a payment attempt initiated from our system.
    /// Webhooks update this record. Finalization reads from here.
    /// </summary>
    public class PaymentIntent
    {
        public int Id { get; set; }

        public PaymentProvider Provider { get; set; } = PaymentProvider.Mpesa;
        public PaymentPurpose Purpose { get; set; }

        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        // Who is paying (optional depending on purpose)
        public int? UserId { get; set; }
        public int? InstitutionId { get; set; }

        // What is being paid for (optional depending on purpose)
        public int? RegistrationIntentId { get; set; }
        public int? ContentProductId { get; set; }

        // Amount & currency
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "KES";

        // ------------------ MPESA identifiers for idempotency ------------------
        public string? MerchantRequestId { get; set; }
        public string? CheckoutRequestId { get; set; }
        public string? MpesaReceiptNumber { get; set; }

        // Payer data
        public string? PhoneNumber { get; set; }

        // Raw callback info (for audit/debug)
        public string? ProviderResultCode { get; set; }
        public string? ProviderResultDesc { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // ------------------ GOVERNANCE ------------------

        /// <summary>
        /// Bank slip number / reference / EFT ref etc.
        /// For Mpesa, you can store receipt in MpesaReceiptNumber.
        /// </summary>
        public string? ManualReference { get; set; }

        /// <summary>
        /// Payment channel used. Mpesa vs Bank transfer vs admin override.
        /// </summary>
        public PaymentMethod Method { get; set; } = PaymentMethod.Mpesa;

        /// <summary>
        /// Optional notes used by admins during approvals.
        /// </summary>
        public string? AdminNotes { get; set; }

        public int? ApprovedByUserId { get; set; }
        public User? ApprovedByUser { get; set; }
        public DateTime? ApprovedAt { get; set; }

        public bool IsFinalized { get; set; } = false;

        public int? DurationInMonths { get; set; }

        // ✅ NEW: Legal document purchase
        public int? LegalDocumentId { get; set; }

            public string? ProviderReference { get; set; }

    
        public string? ProviderTransactionId { get; set; }


        public string? ProviderChannel { get; set; }


        public DateTime? ProviderPaidAt { get; set; }

        public string? ProviderRawJson { get; set; }

        public int? InvoiceId { get; set; }
        public Invoice? Invoice { get; set; }
        public string? ClientReturnUrl { get; set; }

    }
}
