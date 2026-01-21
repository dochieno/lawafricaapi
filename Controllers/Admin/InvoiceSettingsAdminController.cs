using LawAfrica.API.Controllers.Admin;
using LawAfrica.API.Data;
using LawAfrica.API.Models.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
// InvoiceSettingsAdminController.cs
using Microsoft.Extensions.Configuration;

[ApiController]
[Route("api/admin/invoice-settings")]
[Authorize(Roles = "Admin")]
public class InvoiceSettingsAdminController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;

    public InvoiceSettingsAdminController(ApplicationDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    private string GetStorageRoot()
    {
        var root = _config["STORAGE_ROOT"];
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(AppContext.BaseDirectory, "Storage");
        return root;
    }

    private static string? NormalizeLogoUrl(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;

        var s = v.Trim().Replace('\\', '/');

        // already canonical
        if (s.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
            return s;

        // legacy "Storage/..."
        if (s.StartsWith("storage/", StringComparison.OrdinalIgnoreCase))
            return "/storage/" + s.Substring("storage/".Length).TrimStart('/');

        // filename-only fallback
        if (!s.Contains("/"))
            return "/storage/Invoice/" + s;

        return s.StartsWith("/") ? s : "/" + s;
    }

    private static string? ResolveLogoDiskPath(string storageRoot, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;

        var s = stored.Trim().Replace('\\', '/');

        if (s.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
        {
            var rel = s.Substring("/storage/".Length).TrimStart('/');
            return Path.Combine(storageRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        }

        if (s.StartsWith("storage/", StringComparison.OrdinalIgnoreCase))
        {
            var rel = s.Substring("storage/".Length).TrimStart('/');
            return Path.Combine(storageRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        }

        if (!s.Contains("/"))
            return Path.Combine(storageRoot, "Invoice", s);

        return Path.Combine(storageRoot, s.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
    }

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

        var storageRoot = GetStorageRoot();
        var diskDir = Path.Combine(storageRoot, "Invoice");
        Directory.CreateDirectory(diskDir);

        var s = await _db.InvoiceSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (s == null)
        {
            s = new InvoiceSettings { Id = 1, CompanyName = "LawAfrica" };
            _db.InvoiceSettings.Add(s);
        }

        // ✅ delete old logo if any
        if (!string.IsNullOrWhiteSpace(s.LogoPath))
        {
            var oldDisk = ResolveLogoDiskPath(storageRoot, s.LogoPath);
            if (!string.IsNullOrWhiteSpace(oldDisk) && System.IO.File.Exists(oldDisk))
                System.IO.File.Delete(oldDisk);
        }

        // ✅ unique filename to avoid caching issues
        var filename = $"invoice_logo_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}";
        var fullPath = Path.Combine(diskDir, filename);

        await using (var fs = new FileStream(fullPath, FileMode.Create))
            await file.CopyToAsync(fs, ct);

        // ✅ canonical URL path (must match Program.cs "/storage" mapping)
        var urlPath = $"/storage/Invoice/{filename}";
        s.LogoPath = urlPath;
        s.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            ok = true,
            logoPath = s.LogoPath,
            logoUrl = $"{Request.Scheme}://{Request.Host}{urlPath}"
        });
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
        LogoPath = NormalizeLogoUrl(s.LogoPath), // ✅ normalize for frontend
        BankName = s.BankName,
        BankAccountName = s.BankAccountName,
        BankAccountNumber = s.BankAccountNumber,
        PaybillNumber = s.PaybillNumber,
        TillNumber = s.TillNumber,
        AccountReference = s.AccountReference,
        FooterNotes = s.FooterNotes
    };
}

