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

            // ✅ Virtual root is what goes into DB (and what your frontend expects)
            _virtualRoot = (config["Storage:VirtualRoot"] ?? "Storage").Trim().Trim('/', '\\');

            // Subfolders inside the physical root
            var legalDocsFolder = (config["Storage:LegalDocuments"] ?? "LegalDocuments").Trim().Trim('/', '\\');
            var coversFolder = (config["Storage:Covers"] ?? "Covers").Trim().Trim('/', '\\');

            // ✅ Physical root:
            // If STORAGE_ROOT exists (Render disk mount), store there.
            // Else local/dev fallback: ./Storage under app root
            var storageRootEnv = Environment.GetEnvironmentVariable("STORAGE_ROOT");
            if (!string.IsNullOrWhiteSpace(storageRootEnv))
            {
                _rootPhysicalPath = storageRootEnv.Trim().TrimEnd('/', '\\');
            }
            else
            {
                _rootPhysicalPath = Path.Combine(env.ContentRootPath, "Storage");
            }

            _legalDocsPhysicalPath = Path.Combine(_rootPhysicalPath, legalDocsFolder);
            _coversPhysicalPath = Path.Combine(_rootPhysicalPath, coversFolder);

            Directory.CreateDirectory(_rootPhysicalPath);
            Directory.CreateDirectory(_legalDocsPhysicalPath);
            Directory.CreateDirectory(_coversPhysicalPath);

            _logger.LogInformation(
                "Storage ready. PhysicalRoot={Root} | LegalDocs={LegalDocs} | Covers={Covers} | VirtualRoot={VirtualRoot} | STORAGE_ROOT={StorageRootEnv}",
                _rootPhysicalPath, _legalDocsPhysicalPath, _coversPhysicalPath, _virtualRoot, storageRootEnv
            );
        }

        // Used for static files mapping
        public string RootPhysicalPath => _rootPhysicalPath;

        // ✅ Convert DB path (Storage/...) to a physical path on disk
        public string ResolvePhysicalPathFromDbPath(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                return string.Empty;

            var clean = dbPath.Replace("\\", "/").Trim();

            // Strip VirtualRoot prefix (e.g. "Storage/")
            var vr = _virtualRoot.Trim().Trim('/', '\\');
            var prefix = vr + "/";
            if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(prefix.Length);

            clean = clean.TrimStart('/');

            // clean becomes: "LegalDocuments/xxx.pdf" or "Covers/xxx.jpg"
            return Path.Combine(_rootPhysicalPath, clean.Replace("/", Path.DirectorySeparatorChar.ToString()));
        }

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

            // ✅ DB path stays consistent: Storage/LegalDocuments/...
            var relativePath = $"{_virtualRoot}/LegalDocuments/{fileName}".Replace("\\", "/");
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
            return (relativePath, file.Length);
        }
    }
}
