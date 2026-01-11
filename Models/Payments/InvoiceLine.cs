using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Payments
{
    /// <summary>
    /// Global-standard invoice line.
    /// </summary>
    public class InvoiceLine
    {
        public int Id { get; set; }

        public int InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = null!;

        [MaxLength(200)]
        public string Description { get; set; } = string.Empty;

        // ERP-friendly SKU/item code (optional)
        [MaxLength(50)]
        public string? ItemCode { get; set; }

        public decimal Quantity { get; set; } = 1m;
        public decimal UnitPrice { get; set; }

        // Totals
        public decimal LineSubtotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal LineTotal { get; set; }

        // Optional linkage to your domain objects
        public int? ContentProductId { get; set; }
        public int? LegalDocumentId { get; set; }
    }
}
