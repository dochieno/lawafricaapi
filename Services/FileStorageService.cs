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
        private readonly string _lawReportAttachmentsPhysicalPath;

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

            // ✅ NEW: Law report attachments folder (relative to physical root)
            // Default aligns with DB path: Storage/LawReports/Attachments/...
            var lawReportAttachmentsFolder = (config["Storage:LawReportAttachments"] ?? "LawReports/Attachments")
                .Trim()
                .Trim('/', '\\');

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

            // ✅ NEW: physical path for report attachments (supports nested "LawReports/Attachments")
            _lawReportAttachmentsPhysicalPath = Path.Combine(
                _rootPhysicalPath,
                lawReportAttachmentsFolder.Replace("/", Path.DirectorySeparatorChar.ToString())
                                          .Replace("\\", Path.DirectorySeparatorChar.ToString())
            );

            Directory.CreateDirectory(_rootPhysicalPath);
            Directory.CreateDirectory(_legalDocsPhysicalPath);
            Directory.CreateDirectory(_coversPhysicalPath);
            Directory.CreateDirectory(_lawReportAttachmentsPhysicalPath);

            _logger.LogInformation(
                "Storage ready. PhysicalRoot={Root} | LegalDocs={LegalDocs} | Covers={Covers} | LawReportAttachments={LawReportAttachments} | VirtualRoot={VirtualRoot} | STORAGE_ROOT={StorageRootEnv}",
                _rootPhysicalPath,
                _legalDocsPhysicalPath,
                _coversPhysicalPath,
                _lawReportAttachmentsPhysicalPath,
                _virtualRoot,
                storageRootEnv
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

            // clean becomes: "LegalDocuments/xxx.pdf" or "Covers/xxx.jpg" or "LawReports/Attachments/xxx.pdf"
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

        // ✅ NEW: Save Law Report attachment (optional file linked to LawReport)
        // DB path: Storage/LawReports/Attachments/{guid}.{ext}
        public async Task<(string relativePath, long size)> SaveLawReportAttachmentAsync(IFormFile file, string fileType)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("Attachment file is required.");

            // Accept either explicit fileType from controller or derive from filename
            var extFromName = Path.GetExtension(file.FileName).ToLowerInvariant();
            var safeType = (fileType ?? "").Trim().ToLowerInvariant();

            string ext;
            if (!string.IsNullOrWhiteSpace(extFromName))
            {
                ext = extFromName;
            }
            else if (safeType == "pdf") ext = ".pdf";
            else if (safeType == "doc") ext = ".doc";
            else if (safeType == "docx") ext = ".docx";
            else ext = "";

            if (ext != ".pdf" && ext != ".doc" && ext != ".docx")
                throw new InvalidOperationException("Only PDF, DOC, or DOCX attachments are allowed.");

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var physicalPath = Path.Combine(_lawReportAttachmentsPhysicalPath, fileName);

            await using (var stream = new FileStream(physicalPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream);
            }

            // ✅ DB path stays consistent: Storage/LawReports/Attachments/...
            var relativePath = $"{_virtualRoot}/LawReports/Attachments/{fileName}".Replace("\\", "/");
            return (relativePath, file.Length);
        }
    }
}
