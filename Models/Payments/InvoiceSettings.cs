using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Payments
{
    public class InvoiceSettings
    {
        public int Id { get; set; } = 1;

        [MaxLength(200)]
        public string CompanyName { get; set; } = "LawAfrica";

        [MaxLength(200)]
        public string? AddressLine1 { get; set; }

        [MaxLength(200)]
        public string? AddressLine2 { get; set; }

        [MaxLength(120)]
        public string? City { get; set; }

        [MaxLength(120)]
        public string? Country { get; set; }

        [MaxLength(80)]
        public string? VatOrPin { get; set; }

        [MaxLength(200)]
        public string? Email { get; set; }

        [MaxLength(80)]
        public string? Phone { get; set; }

        // Stored path under Storage/ or similar
        [MaxLength(300)]
        public string? LogoPath { get; set; }

        // Payment details printed on invoice
        [MaxLength(120)]
        public string? BankName { get; set; }

        [MaxLength(120)]
        public string? BankAccountName { get; set; }

        [MaxLength(60)]
        public string? BankAccountNumber { get; set; }

        [MaxLength(40)]
        public string? PaybillNumber { get; set; }

        [MaxLength(40)]
        public string? TillNumber { get; set; }

        [MaxLength(80)]
        public string? AccountReference { get; set; }

        [MaxLength(2000)]
        public string? FooterNotes { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
