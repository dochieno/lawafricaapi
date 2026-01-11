using LawAfrica.API.Data;
using LawAfrica.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/documents")]
    [Authorize(Roles = "Admin")]
    public class DocumentIndexingController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocumentIndexingService _indexing;

        public DocumentIndexingController(
            ApplicationDbContext db,
            IDocumentIndexingService indexing)
        {
            _db = db;
            _indexing = indexing;
        }

        [HttpPost("{id:int}/index")]
        public async Task<IActionResult> Index(int id)
        {
            var doc = await _db.LegalDocuments
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null)
                return NotFound();

            if (doc.FileType != "pdf")
                return BadRequest("Only PDF indexing supported.");

            await _indexing.IndexPdfAsync(doc);

            return Ok("Indexing completed.");
        }
    }
}
