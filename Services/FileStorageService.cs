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

        // Folder names (used both for physical subfolders and DB relative paths)
        private readonly string _legalDocsFolder;
        private readonly string _coversFolder;

        // Virtual root (what you store in DB + serve via /storage)
        // Example stored value: "Storage/Covers/abc.png"
        private readonly string _virtualRoot;

        public FileStorageService(IConfiguration config, IWebHostEnvironment env, ILogger<FileStorageService> logger)
        {
            _logger = logger;

            // ✅ Virtual root used in DB paths (frontend maps to /storage/...)
            _virtualRoot = (config["Storage:VirtualRoot"] ?? "Storage").Trim().Trim('/', '\\');

            // ✅ IMPORTANT: Use Render Disk root when available
            // If STORAGE_ROOT is set (e.g., /var/data/Storage), we store files there.
            // Otherwise fall back to app folder (local dev).
            var diskRoot = (Environment.GetEnvironmentVariable("STORAGE_ROOT") ?? "").Trim();

            // Physical root folder name (relative to app root) - used only when STORAGE_ROOT is NOT set
            var rootFolder = (config["Storage:RootPath"] ?? "Storage").Trim().Trim('/', '\\');

            // Physical subfolders (KEEP CASE CONSISTENT: Linux is case-sensitive)
            _legalDocsFolder = (config["Storage:LegalDocuments"] ?? "LegalDocuments").Trim().Trim('/', '\\');
            _coversFolder = (config["Storage:Covers"] ?? "Covers").Trim().Trim('/', '\\');

            // ✅ Decide physical root
            // - If STORAGE_ROOT is absolute, use it directly
            // - Else use ContentRootPath + rootFolder
            if (!string.IsNullOrWhiteSpace(diskRoot))
            {
                // If someone sets STORAGE_ROOT to "/var/data" instead of "/var/data/Storage",
                // we’ll respect it as-is and store subfolders under it.
                _rootPhysicalPath = diskRoot;
            }
            else
            {
                // local/dev default: <app>/Storage
                _rootPhysicalPath = Path.Combine(env.ContentRootPath, rootFolder);
            }

            // Build physical subfolder paths
            _legalDocsPhysicalPath = Path.Combine(_rootPhysicalPath, _legalDocsFolder);
            _coversPhysicalPath = Path.Combine(_rootPhysicalPath, _coversFolder);

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
                    string.IsNullOrWhiteSpace(diskRoot) ? "(not set)" : diskRoot
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create storage directories. Root={Root}", _rootPhysicalPath);
                throw; // real failure (disk permissions etc.)
            }
        }

        // Used for static files mapping (if you ever want to wire Program.cs to this)
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

            // ✅ Store DB path under VirtualRoot + SAME folder name we used physically
            // so frontend can resolve /storage/<folder>/<file>
            var relativePath = $"{_virtualRoot}/{_legalDocsFolder}/{fileName}".Replace("\\", "/");
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

            var relativePath = $"{_virtualRoot}/{_coversFolder}/{fileName}".Replace("\\", "/");
            return (relativePath, file.Length);
        }
    }
}
