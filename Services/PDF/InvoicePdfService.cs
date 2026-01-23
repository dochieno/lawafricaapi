using System.Globalization;
using LawAfrica.API.Data;
using LawAfrica.API.Models.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LawAfrica.API.Services.Payments
{
    /// <summary>
    /// Generates invoice PDFs as byte[] for:
    /// - Admin download
    /// - Customer download
    /// - Email attachments
    /// </summary>
    public class InvoicePdfService
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<InvoicePdfService> _logger;

        public InvoicePdfService(ApplicationDbContext db, IWebHostEnvironment env, ILogger<InvoicePdfService> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;

            // ✅ Prevent QuestPDF license exception at runtime (safe to set here too)
            QuestPDF.Settings.License = LicenseType.Community;
        }

        /// <summary>
        /// Loads invoice + settings, then generates PDF bytes.
        /// </summary>
        public async Task<byte[]> GenerateInvoicePdfAsync(int invoiceId, CancellationToken ct)
        {
            var invoice = await _db.Invoices
                .AsNoTracking()
                .Include(i => i.Lines)
                .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

            if (invoice == null)
                throw new InvalidOperationException($"Invoice #{invoiceId} not found.");

            var settings = await _db.InvoiceSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == 1, ct);

            settings ??= new InvoiceSettings { Id = 1, CompanyName = "LawAfrica" };

            return GenerateInvoicePdfBytes(invoice, settings);
        }

        /// <summary>
        /// Loads invoice + settings, and also returns a suggested filename.
        /// Useful for controllers and attachments.
        /// </summary>
        public async Task<(byte[] Pdf, string FileName)> GenerateInvoicePdfWithFileNameAsync(int invoiceId, CancellationToken ct)
        {
            var invoice = await _db.Invoices
                .AsNoTracking()
                .Include(i => i.Lines)
                .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

            if (invoice == null)
                throw new InvalidOperationException($"Invoice #{invoiceId} not found.");

            var settings = await _db.InvoiceSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == 1, ct);

            settings ??= new InvoiceSettings { Id = 1, CompanyName = "LawAfrica" };

            var pdf = GenerateInvoicePdfBytes(invoice, settings);
            var invNo = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? $"INV-{invoice.Id}" : invoice.InvoiceNumber.Trim();
            var fileName = $"{invNo}.pdf";

            return (pdf, fileName);
        }

        /// <summary>
        /// Generates PDF bytes from already loaded models (useful for email/webhooks).
        /// </summary>
        public byte[] GenerateInvoicePdfBytes(Invoice invoice, InvoiceSettings settings)
        {
            // If you prefer strict currency formatting per currency, you can extend this.
            var culture = CultureInfo.InvariantCulture;

            // VAT message based on stored totals (no guessing beyond what invoice stores)
            var vatNote = BuildVatNote(invoice);

            // Logo (optional)
            byte[]? logoBytes = null;
            try
            {
                var logoPath = ResolveLogoAbsolutePath(settings.LogoPath);
                if (!string.IsNullOrWhiteSpace(logoPath) && System.IO.File.Exists(logoPath))
                    logoBytes = System.IO.File.ReadAllBytes(logoPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load invoice logo.");
            }

            var created = invoice.CreatedAt == default ? DateTime.UtcNow : invoice.CreatedAt;
            var issued = invoice.IssuedAt == default ? created : invoice.IssuedAt;

            var invNo = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? $"INV-{invoice.Id}" : invoice.InvoiceNumber.Trim();

            // Defensive ordering (stable PDF)
            var lines = (invoice.Lines ?? new List<InvoiceLine>())
                .OrderBy(l => l.Id)
                .ToList();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Helvetica"));

                    page.Header().Element(h =>
                    {
                        h.Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text(settings.CompanyName ?? "LawAfrica")
                                    .FontSize(18).SemiBold();

                                var addr = BuildCompanyAddress(settings);
                                if (!string.IsNullOrWhiteSpace(addr))
                                    col.Item().Text(addr).FontSize(10).FontColor(Colors.Grey.Darken2);

                                var contacts = BuildCompanyContacts(settings);
                                if (!string.IsNullOrWhiteSpace(contacts))
                                    col.Item().Text(contacts).FontSize(10).FontColor(Colors.Grey.Darken2);

                                if (!string.IsNullOrWhiteSpace(settings.VatOrPin))
                                    col.Item().Text($"VAT/PIN: {settings.VatOrPin}")
                                        .FontSize(10).SemiBold().FontColor(Colors.Grey.Darken3);
                            });

                            row.ConstantItem(170).AlignRight().Column(col =>
                            {
                                if (logoBytes != null)
                                {
                                    col.Item().AlignRight().Height(48).Image(logoBytes);
                                    col.Item().PaddingTop(6);
                                }

                                col.Item().AlignRight().Text("INVOICE").FontSize(18).SemiBold();
                                col.Item().AlignRight().Text($"Invoice #: {invNo}").FontSize(10).SemiBold();

                                col.Item().AlignRight().Text($"Status: {invoice.Status}").FontSize(10);
                                col.Item().AlignRight().Text($"Issued: {issued:dd-MMM-yyyy}").FontSize(10);

                                if (invoice.DueAt.HasValue)
                                    col.Item().AlignRight().Text($"Due: {invoice.DueAt.Value:dd-MMM-yyyy}")
                                        .FontSize(10);

                                if (invoice.PaidAt.HasValue)
                                    col.Item().AlignRight().Text($"Paid: {invoice.PaidAt.Value:dd-MMM-yyyy}")
                                        .FontSize(10);
                            });
                        });

                        // divider
                        h.PaddingTop(12).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    page.Content().PaddingTop(14).Column(col =>
                    {
                        col.Spacing(12);

                        // Bill to + Summary
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Billed To").SemiBold().FontSize(11);

                                var customer = !string.IsNullOrWhiteSpace(invoice.CustomerName)
                                    ? invoice.CustomerName!.Trim()
                                    : ResolveFallbackCustomer(invoice);

                                c.Item().Text(customer).FontSize(10);

                                if (invoice.InstitutionId.HasValue)
                                    c.Item().Text($"Institution ID: {invoice.InstitutionId.Value}")
                                        .FontSize(9).FontColor(Colors.Grey.Darken2);

                                if (invoice.UserId.HasValue)
                                    c.Item().Text($"User ID: {invoice.UserId.Value}")
                                        .FontSize(9).FontColor(Colors.Grey.Darken2);

                                c.Item().Text($"Purpose: {invoice.Purpose}")
                                    .FontSize(9).FontColor(Colors.Grey.Darken2);
                            });

                            r.ConstantItem(240).AlignRight().Column(c =>
                            {
                                c.Item().Text("Summary").SemiBold().FontSize(11);

                                var cur = string.IsNullOrWhiteSpace(invoice.Currency) ? "KES" : invoice.Currency.Trim();
                                c.Item().Text($"Currency: {cur}").FontSize(10);

                                if (!string.IsNullOrWhiteSpace(vatNote))
                                    c.Item().Text(vatNote)
                                        .FontSize(9)
                                        .FontColor(Colors.Grey.Darken2);
                            });
                        });

                        // Line items table
                        col.Item().Element(e =>
                        {
                            e.Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(6); // description
                                    columns.RelativeColumn(2); // qty
                                    columns.RelativeColumn(3); // unit
                                    columns.RelativeColumn(3); // tax
                                    columns.RelativeColumn(3); // total
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(CellHeader).Text("Description");
                                    header.Cell().Element(CellHeader).AlignRight().Text("Qty");
                                    header.Cell().Element(CellHeader).AlignRight().Text("Unit Price");
                                    header.Cell().Element(CellHeader).AlignRight().Text("Tax");
                                    header.Cell().Element(CellHeader).AlignRight().Text("Line Total");
                                });

                                foreach (var l in lines)
                                {
                                    var desc = (l.Description ?? "").Trim();
                                    if (string.IsNullOrWhiteSpace(desc)) desc = "—";

                                    if (!string.IsNullOrWhiteSpace(l.ItemCode))
                                        desc = $"{desc}\nItem: {l.ItemCode!.Trim()}";

                                    table.Cell().Element(CellBody).Text(desc);

                                    table.Cell().Element(CellBody).AlignRight().Text(FormatNum(l.Quantity, culture));

                                    table.Cell().Element(CellBody).AlignRight().Text(FormatMoney(invoice.Currency, l.UnitPrice, culture));

                                    table.Cell().Element(CellBody).AlignRight().Text(FormatMoney(invoice.Currency, l.TaxAmount, culture));

                                    table.Cell().Element(CellBody).AlignRight().Text(FormatMoney(invoice.Currency, l.LineTotal, culture));
                                }
                            });
                        });

                        // Totals
                        col.Item().AlignRight().Column(t =>
                        {
                            t.Item().Row(r =>
                            {
                                r.ConstantItem(180).Text("Subtotal:").AlignRight();
                                r.ConstantItem(120).Text(FormatMoney(invoice.Currency, invoice.Subtotal, culture)).AlignRight().SemiBold();
                            });

                            t.Item().Row(r =>
                            {
                                r.ConstantItem(180).Text("Tax:").AlignRight();
                                r.ConstantItem(120).Text(FormatMoney(invoice.Currency, invoice.TaxTotal, culture)).AlignRight().SemiBold();
                            });

                            if (invoice.DiscountTotal != 0m)
                            {
                                t.Item().Row(r =>
                                {
                                    r.ConstantItem(180).Text("Discount:").AlignRight();
                                    r.ConstantItem(120).Text(FormatMoney(invoice.Currency, invoice.DiscountTotal, culture)).AlignRight().SemiBold();
                                });
                            }

                            t.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                            t.Item().Row(r =>
                            {
                                r.ConstantItem(180).Text("Total:").AlignRight().SemiBold();
                                r.ConstantItem(120).Text(FormatMoney(invoice.Currency, invoice.Total, culture)).AlignRight().SemiBold().FontSize(12);
                            });

                            if (invoice.AmountPaid != 0m)
                            {
                                t.Item().Row(r =>
                                {
                                    r.ConstantItem(180).Text("Amount Paid:").AlignRight();
                                    r.ConstantItem(120).Text(FormatMoney(invoice.Currency, invoice.AmountPaid, culture)).AlignRight().SemiBold();
                                });
                            }

                            if (invoice.Status == InvoiceStatus.Paid && invoice.PaidAt.HasValue)
                            {
                                t.Item().Row(r =>
                                {
                                    r.ConstantItem(180).Text("Paid At:").AlignRight().FontColor(Colors.Grey.Darken2);
                                    r.ConstantItem(120).Text(invoice.PaidAt.Value.ToString("dd-MMM-yyyy")).AlignRight().FontColor(Colors.Grey.Darken2);
                                });
                            }
                        });

                        // Payment details block (optional)
                        var payDetails = BuildPaymentDetails(settings);
                        if (!string.IsNullOrWhiteSpace(payDetails))
                        {
                            col.Item().PaddingTop(2).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(x =>
                            {
                                x.Item().Text("Payment Details").SemiBold().FontSize(11);
                                x.Item().Text(payDetails).FontSize(9).FontColor(Colors.Grey.Darken2);
                            });
                        }

                        // Notes/footer notes
                        if (!string.IsNullOrWhiteSpace(invoice.Notes) || !string.IsNullOrWhiteSpace(settings.FooterNotes))
                        {
                            col.Item().PaddingTop(2).Column(n =>
                            {
                                if (!string.IsNullOrWhiteSpace(invoice.Notes))
                                {
                                    n.Item().Text("Notes").SemiBold().FontSize(11);
                                    n.Item().Text(invoice.Notes!).FontSize(9).FontColor(Colors.Grey.Darken2);
                                }

                                if (!string.IsNullOrWhiteSpace(settings.FooterNotes))
                                {
                                    n.Item().PaddingTop(8);
                                    n.Item().Text(settings.FooterNotes!).FontSize(9).FontColor(Colors.Grey.Darken2);
                                }
                            });
                        }
                    });

                    page.Footer().AlignCenter().Text(txt =>
                    {
                        txt.Span("Generated by LawAfrica • ");
                        txt.Span(DateTime.UtcNow.ToString("dd-MMM-yyyy HH:mm 'UTC'"));
                    });

                    static IContainer CellHeader(IContainer c)
                        => c.DefaultTextStyle(x => x.SemiBold().FontSize(9))
                            .PaddingVertical(6)
                            .PaddingHorizontal(6)
                            .Background(Colors.Grey.Lighten4)
                            .BorderBottom(1).BorderColor(Colors.Grey.Lighten2);

                    static IContainer CellBody(IContainer c)
                        => c.PaddingVertical(6)
                            .PaddingHorizontal(6)
                            .BorderBottom(1).BorderColor(Colors.Grey.Lighten3);
                });
            })
            .GeneratePdf();
        }

        // -------------------------
        // Helpers
        // -------------------------

        private string? ResolveLogoAbsolutePath(string? logoPath)
        {
            if (string.IsNullOrWhiteSpace(logoPath))
                return null;

            var p = logoPath.Trim().Replace("\\", "/");

            // If already absolute
            if (Path.IsPathRooted(p) && File.Exists(p))
                return p;

            // Common storage patterns: "Storage/..." or "/storage/..."
            p = p.TrimStart('/');

            if (p.StartsWith("storage/", StringComparison.OrdinalIgnoreCase))
            {
                // If you store under wwwroot/storage/
                var www = Path.Combine(_env.WebRootPath ?? "", "storage");
                var rel = p.Substring("storage/".Length);
                return Path.Combine(www, rel.Replace("/", Path.DirectorySeparatorChar.ToString()));
            }

            if (p.StartsWith("Storage/", StringComparison.OrdinalIgnoreCase))
            {
                var www = Path.Combine(_env.WebRootPath ?? "", "storage");
                var rel = p.Substring("Storage/".Length);
                return Path.Combine(www, rel.Replace("/", Path.DirectorySeparatorChar.ToString()));
            }

            // Fallback: treat as relative to wwwroot
            return Path.Combine(_env.WebRootPath ?? "", p.Replace("/", Path.DirectorySeparatorChar.ToString()));
        }

        private static string BuildVatNote(Invoice invoice)
        {
            // VAT message must match stored totals (no guessing beyond what invoice stores)
            if (invoice.TaxTotal <= 0m)
                return "VAT: Not applicable";

            // expected total in "exclusive VAT" scenario:
            // Total = Subtotal + TaxTotal - DiscountTotal
            var expectedExclusive = invoice.Subtotal + invoice.TaxTotal - invoice.DiscountTotal;
            if (NearlyEqual(invoice.Total, expectedExclusive))
                return "Prices are VAT exclusive (VAT added at checkout).";

            // common "inclusive VAT" scenario:
            // Total equals subtotal (after discount) while tax is shown separately (informational),
            // or totals already include VAT in line totals.
            var expectedInclusive = invoice.Subtotal - invoice.DiscountTotal;
            if (NearlyEqual(invoice.Total, expectedInclusive))
                return "Prices are VAT inclusive.";

            // Fallback (rare, but don’t lie)
            return "VAT applied (see totals).";
        }

        private static bool NearlyEqual(decimal a, decimal b)
            => Math.Abs(a - b) < 0.01m;

        private static string BuildCompanyAddress(InvoiceSettings s)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(s.AddressLine1)) parts.Add(s.AddressLine1.Trim());
            if (!string.IsNullOrWhiteSpace(s.AddressLine2)) parts.Add(s.AddressLine2.Trim());

            var cityCountry = string.Join(", ", new[]
            {
                (s.City ?? "").Trim(),
                (s.Country ?? "").Trim()
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (!string.IsNullOrWhiteSpace(cityCountry)) parts.Add(cityCountry);

            return string.Join(" • ", parts);
        }

        private static string BuildCompanyContacts(InvoiceSettings s)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(s.Email)) parts.Add(s.Email.Trim());
            if (!string.IsNullOrWhiteSpace(s.Phone)) parts.Add(s.Phone.Trim());
            return string.Join(" • ", parts);
        }

        private static string BuildPaymentDetails(InvoiceSettings s)
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(s.BankName)) lines.Add($"Bank: {s.BankName.Trim()}");
            if (!string.IsNullOrWhiteSpace(s.BankAccountName)) lines.Add($"Account Name: {s.BankAccountName.Trim()}");
            if (!string.IsNullOrWhiteSpace(s.BankAccountNumber)) lines.Add($"Account Number: {s.BankAccountNumber.Trim()}");

            if (!string.IsNullOrWhiteSpace(s.PaybillNumber)) lines.Add($"Paybill: {s.PaybillNumber.Trim()}");
            if (!string.IsNullOrWhiteSpace(s.TillNumber)) lines.Add($"Till: {s.TillNumber.Trim()}");
            if (!string.IsNullOrWhiteSpace(s.AccountReference)) lines.Add($"Reference: {s.AccountReference.Trim()}");

            return string.Join("\n", lines);
        }

        private static string ResolveFallbackCustomer(Invoice invoice)
        {
            if (invoice.InstitutionId.HasValue && invoice.InstitutionId.Value > 0)
                return $"Institution #{invoice.InstitutionId.Value}";

            if (invoice.UserId.HasValue && invoice.UserId.Value > 0)
                return $"User #{invoice.UserId.Value}";

            return "Customer";
        }

        private static string FormatMoney(string? currency, decimal amount, CultureInfo culture)
        {
            var cur = string.IsNullOrWhiteSpace(currency) ? "KES" : currency.Trim();
            // currency displayed explicitly to avoid wrong symbols
            return $"{cur} {amount.ToString("N2", culture)}";
        }

        private static string FormatNum(decimal n, CultureInfo culture)
            => n.ToString("N2", culture).TrimEnd('0').TrimEnd('.');
    }
}
