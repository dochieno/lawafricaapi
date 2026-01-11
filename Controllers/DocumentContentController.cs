using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/documents")]
    [Authorize]
    public class DocumentContentController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;

        public DocumentContentController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // --------------------------------------------------
        // GET: /api/documents/{id}/content
        // --------------------------------------------------
        [HttpGet("{id:int}/content")]
        public async Task<IActionResult> GetContent(int id)
        {
            var document = await _db.LegalDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

            if (document == null)
                return NotFound("Document not found.");

            if (!System.IO.File.Exists(document.FilePath))
                return NotFound("Document file missing.");

            var contentType = document.FileType.ToLower() switch
            {
                "pdf" => "application/pdf",
                "epub" => "application/epub+zip",
                _ => "application/octet-stream"
            };

            var stream = new FileStream(
                document.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );

            // Enable range requests (CRITICAL for PDF readers)
            return File(stream, contentType, enableRangeProcessing: true);
        }

        // GET: /api/documents/{id}/reader-state
        [HttpGet("{id:int}/reader-state")]
        public async Task<IActionResult> GetReaderState(int id)
        {
            var userId = User.GetUserId();

            var document = await _db.LegalDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

            if (document == null)
                return NotFound();

            var progress = await _db.LegalDocumentProgress
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.UserId == userId &&
                    p.LegalDocumentId == id);

            return Ok(new
            {
                document.Id,
                document.Title,
                document.FileType,
                document.PageCount,
                Resume = progress == null ? null : new
                {
                    progress.PageNumber,
                    progress.Cfi,
                    progress.CharOffset,
                    progress.Percentage,
                    progress.IsCompleted
                }
            });
        }

        // GET: /api/documents/{id}/toc
        [HttpGet("{id:int}/toc")]
        public async Task<IActionResult> GetToc(int id)
        {
            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(doc.TableOfContentsJson))
                return Ok(Array.Empty<object>());

            return Content(doc.TableOfContentsJson, "application/json");
        }


    }
}
