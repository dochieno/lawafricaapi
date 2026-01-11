using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace LawAfrica.API.Services
{
    public class FileStorageService
    {
        private readonly string _rootPath;
        private readonly string _legalDocumentsPath;
        private readonly string _coversPath;

        public FileStorageService(IConfiguration config)
        {
            var root = config["Storage:RootPath"];
            var legalDocs = config["Storage:LegalDocuments"];
            var covers = config["Storage:Covers"];

            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("Storage:RootPath is missing in appsettings.json");

            if (string.IsNullOrWhiteSpace(legalDocs))
                throw new InvalidOperationException("Storage:LegalDocuments is missing in appsettings.json");

            if (string.IsNullOrWhiteSpace(covers))
                throw new InvalidOperationException("Storage:Covers is missing in appsettings.json");

            _rootPath = Path.Combine(Directory.GetCurrentDirectory(), root);
            _legalDocumentsPath = Path.Combine(_rootPath, legalDocs);
            _coversPath = Path.Combine(_rootPath, covers);

            Directory.CreateDirectory(_rootPath);
            Directory.CreateDirectory(_legalDocumentsPath);
            Directory.CreateDirectory(_coversPath);
        }

        // Used for serving static files mapping
        public string RootPhysicalPath => _rootPath;

        // Save ebook files (you already use this pattern)
        public async Task<(string relativePath, long size)> SaveLegalDocumentAsync(IFormFile file, string fileType)
        {
            var safeExt = fileType.ToLower() == "pdf" ? ".pdf" : ".epub";
            var fileName = $"{Guid.NewGuid():N}{safeExt}";
            var physicalPath = Path.Combine(_legalDocumentsPath, fileName);

            await using var stream = new FileStream(physicalPath, FileMode.Create);
            await file.CopyToAsync(stream);

            var relativePath = Path.Combine("Storage", "LegalDocuments", fileName).Replace("\\", "/");
            return (relativePath, file.Length);
        }

        // Save cover image
        public async Task<(string relativePath, long size)> SaveCoverAsync(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".webp")
                throw new InvalidOperationException("Cover must be jpg, png, or webp.");

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var physicalPath = Path.Combine(_coversPath, fileName);

            await using var stream = new FileStream(physicalPath, FileMode.Create);
            await file.CopyToAsync(stream);

            var relativePath = Path.Combine("Storage", "Covers", fileName).Replace("\\", "/");
            return (relativePath, file.Length);
        }
    }
}
