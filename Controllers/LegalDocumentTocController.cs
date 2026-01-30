using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Services.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/legal-documents/{id:int}/toc")]
    public class LegalDocumentTocController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly LegalDocumentTocService _toc;

        public LegalDocumentTocController(ApplicationDbContext db, LegalDocumentTocService toc)
        {
            _db = db;
            _toc = toc;
        }

        // Reader uses this (sidebar/drawer)
        // Public because it doesn't reveal full content; only headings + navigation
        [HttpGet]
        public async Task<IActionResult> Get(int id)
        {
            var exists = await _db.LegalDocuments
                .AsNoTracking()
                .AnyAsync(d => d.Id == id && d.Status == LegalDocumentStatus.Published);

            if (!exists) return NotFound("Document not found.");

            var tree = await _toc.GetTreeAsync(id, includeAdminFields: false);
            return Ok(new { items = tree });
        }
    }
}
