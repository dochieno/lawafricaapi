using LawAfrica.API.Data;
using LawAfrica.API.Models.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/invoices")]
    [Authorize(Roles = "Admin")]
    public class InvoicesAdminController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public InvoicesAdminController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET api/admin/invoices?q=&status=&from=&to=&customer=&page=1&pageSize=20
        [HttpGet]
        public async Task<ActionResult<PagedResultDto<InvoiceListItemDto>>> List(
            [FromQuery] string? q,
            [FromQuery] InvoiceStatus? status,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? customer, // name/email fragment
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 5 or > 200 ? 20 : pageSize;

            var query = _db.Invoices.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var s = q.Trim();
                query = query.Where(x =>
                    x.InvoiceNumber.Contains(s) ||
                    (x.ExternalInvoiceNumber != null && x.ExternalInvoiceNumber.Contains(s)) ||
                    (x.CustomerName != null && x.CustomerName.Contains(s)));
            }

            if (status.HasValue)
                query = query.Where(x => x.Status == status.Value);

            if (from.HasValue)
                query = query.Where(x => x.IssuedAt >= from.Value.ToUniversalTime());

            if (to.HasValue)
                query = query.Where(x => x.IssuedAt <= to.Value.ToUniversalTime());

            if (!string.IsNullOrWhiteSpace(customer))
            {
                var c = customer.Trim();
                query = query.Where(x =>
                    (x.CustomerName != null && x.CustomerName.Contains(c)) ||
                    (x.User != null && x.User.Email.Contains(c)) ||
                    (x.Institution != null && (x.Institution.Name.Contains(c) || x.Institution.OfficialEmail.Contains(c))));
            }

            // include only when needed (projection can still reference navs if EF builds join)
            query = query
                .Include(x => x.User)
                .Include(x => x.Institution);

            var total = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(x => x.IssuedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new InvoiceListItemDto
                {
                    Id = x.Id,
                    InvoiceNumber = x.InvoiceNumber,
                    Status = x.Status,
                    Currency = x.Currency,
                    Total = x.Total,
                    AmountPaid = x.AmountPaid,
                    IssuedAt = x.IssuedAt,
                    PaidAt = x.PaidAt,
                    CustomerName = x.CustomerName ?? (x.Institution != null ? x.Institution.Name : (x.User != null ? x.User.Username : null)),
                    CustomerType = x.InstitutionId != null ? "Institution" : (x.UserId != null ? "User" : null),
                    UserId = x.UserId,
                    InstitutionId = x.InstitutionId,
                    Purpose = x.Purpose.ToString()
                })
                .ToListAsync(ct);

            return Ok(new PagedResultDto<InvoiceListItemDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            });
        }

        // GET api/admin/invoices/123
        [HttpGet("{id:int}")]
        public async Task<ActionResult<InvoiceDetailDto>> Get(int id, CancellationToken ct)
        {
            var inv = await _db.Invoices
                .AsNoTracking()
                .Include(x => x.Lines)
                .Include(x => x.User)
                .Include(x => x.Institution)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (inv == null) return NotFound();

            var settings = await _db.InvoiceSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
            settings ??= new InvoiceSettings { Id = 1, CompanyName = "LawAfrica" };

            // Try best-effort customer fields (works even without snapshot columns)
            string? customerName =
                inv.CustomerName ??
                (inv.Institution != null ? inv.Institution.Name :
                 inv.User != null ? inv.User.Username : null);

            string? customerEmail =
                (inv.Institution != null ? inv.Institution.OfficialEmail :
                 inv.User != null ? inv.User.Email : null);

            // If you later add snapshot columns, you can prefer them here (kept safe)
            // Example:
            // customerEmail = inv.CustomerEmail ?? customerEmail;

            var dto = new InvoiceDetailDto
            {
                Id = inv.Id,
                InvoiceNumber = inv.InvoiceNumber,
                Status = inv.Status,
                Currency = inv.Currency,
                IssuedAt = inv.IssuedAt,
                DueAt = inv.DueAt,
                PaidAt = inv.PaidAt,

                Subtotal = inv.Subtotal,
                TaxTotal = inv.TaxTotal,
                DiscountTotal = inv.DiscountTotal,
                Total = inv.Total,
                AmountPaid = inv.AmountPaid,

                Purpose = inv.Purpose.ToString(),
                Notes = inv.Notes,
                ExternalInvoiceNumber = inv.ExternalInvoiceNumber,

                CustomerName = customerName,
                CustomerType = inv.InstitutionId != null ? "Institution" : (inv.UserId != null ? "User" : null),
                CustomerEmail = customerEmail,
                CustomerPhone = inv.User != null ? inv.User.PhoneNumber : null,
                CustomerAddress = null,
                CustomerVatOrPin = null,

                UserId = inv.UserId,
                InstitutionId = inv.InstitutionId,

                Lines = inv.Lines.OrderBy(l => l.Id).Select(l => new InvoiceLineDto
                {
                    Description = l.Description,
                    ItemCode = l.ItemCode,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    LineSubtotal = l.LineSubtotal,
                    TaxAmount = l.TaxAmount,
                    DiscountAmount = l.DiscountAmount,
                    LineTotal = l.LineTotal,
                    ContentProductId = l.ContentProductId,
                    LegalDocumentId = l.LegalDocumentId
                }).ToList(),

                Company = new InvoiceSettingsDto
                {
                    Id = settings.Id,
                    CompanyName = settings.CompanyName,
                    AddressLine1 = settings.AddressLine1,
                    AddressLine2 = settings.AddressLine2,
                    City = settings.City,
                    Country = settings.Country,
                    VatOrPin = settings.VatOrPin,
                    Email = settings.Email,
                    Phone = settings.Phone,
                    LogoPath = settings.LogoPath,
                    BankName = settings.BankName,
                    BankAccountName = settings.BankAccountName,
                    BankAccountNumber = settings.BankAccountNumber,
                    PaybillNumber = settings.PaybillNumber,
                    TillNumber = settings.TillNumber,
                    AccountReference = settings.AccountReference,
                    FooterNotes = settings.FooterNotes
                }
            };

            return Ok(dto);
        }

        // PUT api/admin/invoices/123/status
        [HttpPut("{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateInvoiceStatusRequest req, CancellationToken ct)
        {
            var inv = await _db.Invoices.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (inv == null) return NotFound();

            inv.Status = req.Status;
            if (!string.IsNullOrWhiteSpace(req.Notes))
                inv.Notes = req.Notes!.Trim();

            inv.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return Ok(new { ok = true });
        }
    }
}
