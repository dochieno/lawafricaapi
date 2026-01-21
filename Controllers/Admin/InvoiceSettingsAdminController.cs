using LawAfrica.API.Data;
using LawAfrica.API.Models.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/invoice-settings")]
    [Authorize(Roles = "Admin")]
    public class InvoiceSettingsAdminController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public InvoiceSettingsAdminController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<InvoiceSettingsDto>> Get(CancellationToken ct)
        {
            var s = await _db.InvoiceSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
            s ??= new InvoiceSettings { Id = 1, CompanyName = "LawAfrica" };

            return Ok(ToDto(s));
        }

        [HttpPut]
        public async Task<ActionResult<InvoiceSettingsDto>> Upsert([FromBody] InvoiceSettingsDto dto, CancellationToken ct)
        {
            var s = await _db.InvoiceSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
            if (s == null)
            {
                s = new InvoiceSettings { Id = 1 };
                _db.InvoiceSettings.Add(s);
            }

            s.CompanyName = (dto.CompanyName ?? "LawAfrica").Trim();
            s.AddressLine1 = dto.AddressLine1?.Trim();
            s.AddressLine2 = dto.AddressLine2?.Trim();
            s.City = dto.City?.Trim();
            s.Country = dto.Country?.Trim();
            s.VatOrPin = dto.VatOrPin?.Trim();
            s.Email = dto.Email?.Trim();
            s.Phone = dto.Phone?.Trim();

            s.BankName = dto.BankName?.Trim();
            s.BankAccountName = dto.BankAccountName?.Trim();
            s.BankAccountNumber = dto.BankAccountNumber?.Trim();
            s.PaybillNumber = dto.PaybillNumber?.Trim();
            s.TillNumber = dto.TillNumber?.Trim();
            s.AccountReference = dto.AccountReference?.Trim();

            s.FooterNotes = dto.FooterNotes?.Trim();

            // LogoPath is set by the logo upload endpoint OR you can allow here.
            // Keep safe:
            if (!string.IsNullOrWhiteSpace(dto.LogoPath))
                s.LogoPath = dto.LogoPath.Trim();

            s.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(s));
        }

        // OPTIONAL: Logo upload (multipart)
        // POST api/admin/invoice-settings/logo
        [HttpPost("logo")]
        [RequestSizeLimit(5_000_000)]
        public async Task<ActionResult> UploadLogo([FromForm] IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return BadRequest("File is required.");
            if (file.Length > 5_000_000) return BadRequest("Max 5MB.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new[] { ".png", ".jpg", ".jpeg", ".webp" };
            if (!allowed.Contains(ext)) return BadRequest("Only png/jpg/webp allowed.");

            // NOTE: Plug into your existing storage approach.
            // Minimal local save example:
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "Invoice");
            Directory.CreateDirectory(folder);

            var name = $"invoice-logo{ext}";
            var fullPath = Path.Combine(folder, name);

            await using (var fs = new FileStream(fullPath, FileMode.Create))
                await file.CopyToAsync(fs, ct);

            // Saved path stored as "Storage/Invoice/invoice-logo.png" style
            var storedPath = Path.Combine("storage", "invoice", name).Replace("\\", "/");

            var s = await _db.InvoiceSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
            if (s == null)
            {
                s = new InvoiceSettings { Id = 1, CompanyName = "LawAfrica" };
                _db.InvoiceSettings.Add(s);
            }

            s.LogoPath = storedPath;
            s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return Ok(new { ok = true, logoPath = storedPath });
        }

        private static InvoiceSettingsDto ToDto(InvoiceSettings s) => new()
        {
            Id = s.Id,
            CompanyName = s.CompanyName,
            AddressLine1 = s.AddressLine1,
            AddressLine2 = s.AddressLine2,
            City = s.City,
            Country = s.Country,
            VatOrPin = s.VatOrPin,
            Email = s.Email,
            Phone = s.Phone,
            LogoPath = s.LogoPath,
            BankName = s.BankName,
            BankAccountName = s.BankAccountName,
            BankAccountNumber = s.BankAccountNumber,
            PaybillNumber = s.PaybillNumber,
            TillNumber = s.TillNumber,
            AccountReference = s.AccountReference,
            FooterNotes = s.FooterNotes
        };
    }
}
