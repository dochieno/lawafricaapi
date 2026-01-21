using LawAfrica.API.Models.Payments;

namespace LawAfrica.API.Controllers.Admin
{
    public class InvoiceListItemDto
    {
        public int Id { get; set; }
        public string InvoiceNumber { get; set; } = "";
        public InvoiceStatus Status { get; set; }
        public string Currency { get; set; } = "KES";
        public decimal Total { get; set; }
        public decimal AmountPaid { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime? PaidAt { get; set; }

        public string? CustomerName { get; set; }
        public string? CustomerType { get; set; } // "User" | "Institution" | null
        public int? UserId { get; set; }
        public int? InstitutionId { get; set; }

        public string Purpose { get; set; } = "";
    }

    public class PagedResultDto<T>
    {
        public List<T> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
    }

    public class UpdateInvoiceStatusRequest
    {
        public InvoiceStatus Status { get; set; }
        public string? Notes { get; set; }
    }

    public class InvoiceSettingsDto
    {
        public int Id { get; set; } = 1;
        public string CompanyName { get; set; } = "LawAfrica";
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? VatOrPin { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? LogoPath { get; set; }

        public string? BankName { get; set; }
        public string? BankAccountName { get; set; }
        public string? BankAccountNumber { get; set; }
        public string? PaybillNumber { get; set; }
        public string? TillNumber { get; set; }
        public string? AccountReference { get; set; }

        public string? FooterNotes { get; set; }
    }

    public class InvoiceDetailDto
    {
        public int Id { get; set; }
        public string InvoiceNumber { get; set; } = "";
        public InvoiceStatus Status { get; set; }
        public string Currency { get; set; } = "KES";
        public DateTime IssuedAt { get; set; }
        public DateTime? DueAt { get; set; }
        public DateTime? PaidAt { get; set; }

        public decimal Subtotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal Total { get; set; }
        public decimal AmountPaid { get; set; }

        public string Purpose { get; set; } = "";
        public string? Notes { get; set; }
        public string? ExternalInvoiceNumber { get; set; }

        public string? CustomerName { get; set; }
        public string? CustomerType { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerAddress { get; set; }
        public string? CustomerVatOrPin { get; set; }

        public int? UserId { get; set; }
        public int? InstitutionId { get; set; }

        public List<InvoiceLineDto> Lines { get; set; } = new();

        public InvoiceSettingsDto Company { get; set; } = new();
    }

    public class InvoiceLineDto
    {
        public string Description { get; set; } = "";
        public string? ItemCode { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineSubtotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal LineTotal { get; set; }

        public int? ContentProductId { get; set; }
        public int? LegalDocumentId { get; set; }
    }
}
