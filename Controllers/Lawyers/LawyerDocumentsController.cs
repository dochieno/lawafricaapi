// FILE: LawAfrica.API/Controllers/Lawyers/LawyerDocumentsController.cs
using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models.Lawyers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Lawyers
{
    [ApiController]
    [Route("api/lawyers/me/documents")]
    [Authorize]
    public class LawyerDocumentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _config;

        public LawyerDocumentsController(ApplicationDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        [HttpGet]
        public async Task<IActionResult> ListMine(CancellationToken ct)
        {
            var userId = User.GetUserId();

            var profileId = await _db.LawyerProfiles
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);

            if (profileId == null) return Ok(new List<object>());

            var docs = await _db.LawyerProfileDocuments
                .AsNoTracking()
                .Where(d => d.LawyerProfileId == profileId.Value)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new
                {
                    id = d.Id,
                    type = d.Type.ToString(),
                    typeId = (short)d.Type,
                    fileName = d.FileName,
                    contentType = d.ContentType,
                    sizeBytes = d.SizeBytes,
                    urlPath = d.UrlPath,
                    createdAt = d.CreatedAt
                })
                .ToListAsync(ct);

            return Ok(docs);
        }

        [HttpPost]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> UploadMine(
            [FromForm] IFormFile file,
            [FromForm] LawyerDocumentType type = LawyerDocumentType.Unknown,
            CancellationToken ct = default)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file provided." });

            // allow pdf + images
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "application/pdf",
                "image/jpeg",
                "image/png",
                "image/webp"
            };

            if (!allowed.Contains(file.ContentType))
                return BadRequest(new { message = "Only PDF, JPG, PNG or WEBP allowed." });

            if (file.Length > 10_000_000)
                return BadRequest(new { message = "File must be under 10MB." });

            var userId = User.GetUserId();

            var profile = await _db.LawyerProfiles
                .FirstOrDefaultAsync(x => x.UserId == userId, ct);

            if (profile == null)
                return BadRequest(new { message = "Create your lawyer profile first, then upload documents." });

            var storageRoot = GetStorageRoot();
            var folder = Path.Combine(storageRoot, "LawyerDocs", $"lawyer_{profile.Id}");
            Directory.CreateDirectory(folder);

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext))
                ext = file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ? ".pdf" : ".jpg";

            var safeName = $"{type}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Guid.NewGuid():N}{ext}";
            var diskPath = Path.Combine(folder, safeName);

            using (var stream = new FileStream(diskPath, FileMode.Create))
                await file.CopyToAsync(stream, ct);

            // url path matches your /storage mapping
            var urlPath = $"/storage/LawyerDocs/lawyer_{profile.Id}/{safeName}";

            // Optional: keep only latest per type (common for certificates)
            var existing = await _db.LawyerProfileDocuments
                .Where(d => d.LawyerProfileId == profile.Id && d.Type == type)
                .ToListAsync(ct);

            if (existing.Count > 0)
            {
                // delete old disk files best-effort
                foreach (var old in existing)
                {
                    TryDeleteDiskFile(storageRoot, old.UrlPath);
                    _db.LawyerProfileDocuments.Remove(old);
                }
            }

            var doc = new LawyerProfileDocument
            {
                LawyerProfileId = profile.Id,
                Type = type,
                FileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                UrlPath = urlPath,
                CreatedAt = DateTime.UtcNow
            };

            _db.LawyerProfileDocuments.Add(doc);
            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                id = doc.Id,
                type = doc.Type.ToString(),
                typeId = (short)doc.Type,
                fileName = doc.FileName,
                contentType = doc.ContentType,
                sizeBytes = doc.SizeBytes,
                urlPath = doc.UrlPath
            });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteMine(int id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            var profileId = await _db.LawyerProfiles
                .Where(x => x.UserId == userId)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);

            if (profileId == null) return NotFound();

            var doc = await _db.LawyerProfileDocuments
                .FirstOrDefaultAsync(d => d.Id == id && d.LawyerProfileId == profileId.Value, ct);

            if (doc == null) return NotFound();

            var storageRoot = GetStorageRoot();
            TryDeleteDiskFile(storageRoot, doc.UrlPath);

            _db.LawyerProfileDocuments.Remove(doc);
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        private string GetStorageRoot()
        {
            var root = _config["STORAGE_ROOT"];
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(AppContext.BaseDirectory, "Storage");
            return root;
        }

        private void TryDeleteDiskFile(string storageRoot, string urlPath)
        {
            try
            {
                var p = (urlPath ?? "").Replace('\\', '/').Trim();
                if (p.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
                {
                    var rel = p.Substring("/storage/".Length).TrimStart('/');
                    var disk = Path.Combine(storageRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(disk)) System.IO.File.Delete(disk);
                }
            }
            catch { /* ignore */ }
        }
    }
}