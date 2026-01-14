using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LawAfrica.API.Services
{
    public class FileStorageService
    {
        private readonly ILogger<FileStorageService> _logger;

        // Physical paths (where files are stored on disk)
        private readonly string _rootPhysicalPath;
        private readonly string _legalDocsPhysicalPath;
        private readonly string _coversPhysicalPath;

        // Virtual root (what you store in DB + serve via /storage)
        // Example stored value: "Storage/Covers/abc.png"
        private readonly string _virtualRoot;

        public FileStorageService(IConfiguration config, IWebHostEnvironment env, ILogger<FileStorageService> logger)
        {
            _logger = logger;

            // Virtual root for DB paths (keep default "Storage" to match your frontend + /storage mapping)
            _virtualRoot = (config["Storage:VirtualRoot"] ?? "Storage").Trim().Trim('/', '\\');

            // Disk root override (Render persistent disk)
            // IMPORTANT: this must match Program.cs mapping for /storage
            var diskRoot = Environment.GetEnvironmentVariable("STORAGE_ROOT");

            // Physical root folder name (relative to app root) - used only if STORAGE_ROOT not set
            var rootFolder = (config["Storage:RootPath"] ?? "Storage").Trim().Trim('/', '\\');

            // Physical subfolders
            var legalDocsFolder = (config["Storage:LegalDocuments"] ?? "LegalDocuments").Trim().Trim('/', '\\');
            var coversFolder = (config["Storage:Covers"] ?? "Covers").Trim().Trim('/', '\\');

            // ✅ If STORAGE_ROOT exists, use it as the physical root directly (Render Disk mount)
            // ✅ Else fallback to local ./Storage under ContentRootPath
            _rootPhysicalPath = !string.IsNullOrWhiteSpace(diskRoot)
                ? diskRoot
                : Path.Combine(env.ContentRootPath, rootFolder);

            _legalDocsPhysicalPath = Path.Combine(_rootPhysicalPath, legalDocsFolder);
            _coversPhysicalPath = Path.Combine(_rootPhysicalPath, coversFolder);

            try
            {
                Directory.CreateDirectory(_rootPhysicalPath);
                Directory.CreateDirectory(_legalDocsPhysicalPath);
                Directory.CreateDirectory(_coversPhysicalPath);

                _logger.LogInformation(
                    "Storage ready. PhysicalRoot={Root} | LegalDocs={LegalDocs} | Covers={Covers} | VirtualRoot={VirtualRoot} | STORAGE_ROOT={StorageRootEnv}",
                    _rootPhysicalPath,
                    _legalDocsPhysicalPath,
                    _coversPhysicalPath,
                    _virtualRoot,
                    diskRoot ?? "(null)"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create storage directories. Root={Root}", _rootPhysicalPath);
                throw;
            }
        }

        // Used for static files mapping or debugging
        public string RootPhysicalPath => _rootPhysicalPath;

        // Save ebook files
        public async Task<(string relativePath, long size)> SaveLegalDocumentAsync(IFormFile file, string fileType)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("Document file is required.");

            var safeType = (fileType ?? "").Trim().ToLowerInvariant();
            var safeExt = safeType == "pdf" ? ".pdf" : safeType == "epub" ? ".epub" : null;

            if (safeExt == null)
                throw new InvalidOperationException("Only PDF or EPUB files are allowed.");

            var fileName = $"{Guid.NewGuid():N}{safeExt}";
            var physicalPath = Path.Combine(_legalDocsPhysicalPath, fileName);

            await using (var stream = new FileStream(physicalPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream);
            }

            // DB path (frontend strips "Storage/" and uses /storage)
            var relativePath = $"{_virtualRoot}/LegalDocuments/{fileName}".Replace("\\", "/");

            _logger.LogInformation("Saved legal document: {File} -> {PhysicalPath} (DB={DbPath})",
                file.FileName, physicalPath, relativePath);

            return (relativePath, file.Length);
        }

        // Save cover image
        public async Task<(string relativePath, long size)> SaveCoverAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("Cover file is required.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".webp")
                throw new InvalidOperationException("Cover must be jpg, jpeg, png, or webp.");

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var physicalPath = Path.Combine(_coversPhysicalPath, fileName);

            await using (var stream = new FileStream(physicalPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream);
            }

            var relativePath = $"{_virtualRoot}/Covers/{fileName}".Replace("\\", "/");

            _logger.LogInformation("Saved cover: {File} -> {PhysicalPath} (DB={DbPath})",
                file.FileName, physicalPath, relativePath);

            return (relativePath, file.Length);
        }
    }
}
