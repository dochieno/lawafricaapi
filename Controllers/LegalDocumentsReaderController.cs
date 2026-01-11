using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/legal-documents")]
    public class LegalDocumentsReaderController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _config;

        public LegalDocumentsReaderController(
            ApplicationDbContext db,
            IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        // --------------------------------------------------
        // READ DOCUMENT (PDF / EPUB)
        // --------------------------------------------------
        [Authorize]
        [HttpGet("{id}/read")]
        public async Task<IActionResult> Read(int id)
        {
            var doc = await _db.LegalDocuments.FindAsync(id);
            if (doc == null || doc.Status != LegalDocumentStatus.Published)
                return NotFound("Document not available.");

            // Premium enforcement (extend later with subscriptions)
            if (doc.IsPremium && !User.IsInRole("Admin"))
                return Forbid("Premium document.");

            var root = _config["Storage:LegalDocumentsPath"];
            var fullPath = Path.Combine(root!, doc.FilePath);

            if (!System.IO.File.Exists(fullPath))
                return NotFound("File not found.");

            var mime = doc.FileType == "epub"
                ? "application/epub+zip"
                : "application/pdf";

            return PhysicalFile(
                fullPath,
                mime,
                enableRangeProcessing: true
            );
        }
    }
}
