using LawAfrica.API.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace LawAfrica.API.Models
{
    /// <summary>
    /// Tracks public individual purchases of a LegalDocument.
    /// (Minimal version: no payment provider integration required yet.)
    /// </summary>
    public class UserLegalDocumentPurchase
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int LegalDocumentId { get; set; }
        public LegalDocument LegalDocument { get; set; } = null!;

        public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;

        // optional financial metadata (keep nullable for now)
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
        public string? PaymentReference { get; set; }
    }
}
