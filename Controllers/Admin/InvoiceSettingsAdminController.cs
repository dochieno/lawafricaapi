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
        private readonly IConfiguration _config;

        private const long MaxLogoBytes = 5_000_000; // 5MB

        private static readonly string[] AllowedLogoTypes =
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

        private static readonly string[] AllowedExt =
        {
            ".png", ".jpg", ".jpeg", ".webp"
        };

        public InvoiceSettingsAdminController(ApplicationDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
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

            // LogoPath is primarily set by UploadLogo.
            // Keep safe: allow only if provided (so manual set is still possible).
            if (!string.IsNullOrWhiteSpace(dto.LogoPath))
                s.LogoPath = dto.LogoPath.Trim();

            s.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(s));
        }

        // POST api/admin/invoice-settings/logo (multipart)
        [HttpPost("logo")]
        [RequestSizeLimit(MaxLogoBytes)]
        public async Task<ActionResult> UploadLogo([FromForm] IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File is required.");

            if (file.Length > MaxLogoBytes)
                return BadRequest("Max 5MB.");

            // Prefer MIME validation (browser sets this correctly most times)
            if (!AllowedLogoTypes.Contains(file.ContentType))
                return BadRequest("Only JPG, PNG or WEBP allowed.");

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = file.ContentType switch
                {
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    _ => ".jpg"
                };
            }

            if (!AllowedExt.Contains(ext))
                return BadRequest("Only png/jpg/webp allowed.");

            // Normalize .jpeg -> .jpg to keep deterministic filename stable
            if (ext == ".jpeg") ext = ".jpg";

            // ✅ Must match Program.cs /storage mapping (STORAGE_ROOT)
            var storageRoot = GetStorageRoot();
            var diskDir = Path.Combine(storageRoot, "Invoice");
            Directory.CreateDirectory(diskDir);

            // Upsert settings row (Id=1)
            var s = await _db.InvoiceSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
            if (s == null)
            {
                s = new InvoiceSettings { Id = 1, CompanyName = "LawAfrica" };
                _db.InvoiceSettings.Add(s);
            }

            // Delete old logo file (best-effort)
            if (!string.IsNullOrWhiteSpace(s.LogoPath))
            {
                var oldDisk = ResolveInvoiceLogoDiskPath(storageRoot, s.LogoPath);
                if (!string.IsNullOrWhiteSpace(oldDisk) && System.IO.File.Exists(oldDisk))
                {
                    try { System.IO.File.Delete(oldDisk); } catch { /* ignore */ }
                }
            }

            // Deterministic name (overwrites); frontend can cache-bust via ?v=nonce
            var filename = $"invoice-logo{ext}";
            var fullPath = Path.Combine(diskDir, filename);

            await using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(fs, ct);
            }

            // ✅ Store canonical URL (best for frontend)
            var storedPath = $"/storage/Invoice/{filename}";

            s.LogoPath = storedPath;
            s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return Ok(new { ok = true, logoPath = storedPath });
        }

        // -----------------------------
        // Helpers
        // -----------------------------
        private string GetStorageRoot()
        {
            // Must match Program.cs logic
            var root = _config["STORAGE_ROOT"];
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(Directory.GetCurrentDirectory(), "Storage");
            return root;
        }

        private static string? ResolveInvoiceLogoDiskPath(string storageRoot, string stored)
        {
            var s = (stored ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) return null;

            s = s.Replace('\\', '/');

            // URL form: /storage/Invoice/x.png
            if (s.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
            {
                var rel = s.Substring("/storage/".Length).TrimStart('/');
                if (rel.StartsWith("invoice/", StringComparison.OrdinalIgnoreCase))
                    rel = "Invoice/" + rel.Substring("invoice/".Length);

                return Path.Combine(storageRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            }

            // Disk-ish form: storage/Invoice/x.png
            if (s.StartsWith("storage/", StringComparison.OrdinalIgnoreCase))
            {
                var rel = s.Substring("storage/".Length).TrimStart('/');
                if (rel.StartsWith("invoice/", StringComparison.OrdinalIgnoreCase))
                    rel = "Invoice/" + rel.Substring("invoice/".Length);

                return Path.Combine(storageRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            }

            // filename-only
            if (!s.Contains("/"))
                return Path.Combine(storageRoot, "Invoice", s);

            return Path.Combine(storageRoot, s.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
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
