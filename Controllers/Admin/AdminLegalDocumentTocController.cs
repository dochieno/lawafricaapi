using LawAfrica.API.Authorization.Policies;
using LawAfrica.API.Data;
using LawAfrica.API.Models.DTOs.LegalDocuments.Toc;
using LawAfrica.API.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/legal-documents/{id:int}/toc")]
    [Authorize(Roles = "Admin")]
    public class AdminLegalDocumentTocController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly LegalDocumentTocService _toc;

        public AdminLegalDocumentTocController(ApplicationDbContext db, LegalDocumentTocService toc)
        {
            _db = db;
            _toc = toc;
        }

        // Admin tree (includes Notes)
        [HttpGet]
        public async Task<IActionResult> Get(int id)
        {
            var exists = await _db.LegalDocuments.AsNoTracking().AnyAsync(d => d.Id == id);
            if (!exists) return NotFound("Document not found.");

            var tree = await _toc.GetTreeAsync(id, includeAdminFields: true);
            return Ok(new { items = tree });
        }

        [HttpPost]
        public async Task<IActionResult> Create(int id, [FromBody] TocEntryCreateRequest request)
        {
            var exists = await _db.LegalDocuments.AsNoTracking().AnyAsync(d => d.Id == id);
            if (!exists) return NotFound("Document not found.");

            try
            {
                var created = await _toc.CreateAsync(id, request);
                return Ok(created);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{entryId:int}")]
        public async Task<IActionResult> Update(int id, int entryId, [FromBody] TocEntryUpdateRequest request)
        {
            var exists = await _db.LegalDocuments.AsNoTracking().AnyAsync(d => d.Id == id);
            if (!exists) return NotFound("Document not found.");

            try
            {
                var updated = await _toc.UpdateAsync(id, entryId, request);
                return Ok(updated);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("{entryId:int}")]
        public async Task<IActionResult> Delete(int id, int entryId)
        {
            var exists = await _db.LegalDocuments.AsNoTracking().AnyAsync(d => d.Id == id);
            if (!exists) return NotFound("Document not found.");

            try
            {
                await _toc.DeleteAsync(id, entryId);
                return Ok(new { message = "Deleted." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Drag-drop reorder (and re-parent) uses this
        [HttpPut("reorder")]
        public async Task<IActionResult> Reorder(int id, [FromBody] TocReorderRequest request)
        {
            var exists = await _db.LegalDocuments.AsNoTracking().AnyAsync(d => d.Id == id);
            if (!exists) return NotFound("Document not found.");

            try
            {
                await _toc.ReorderAsync(id, request);
                return Ok(new { message = "Reordered." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Quick upload/import (replace or append)
        [HttpPost("import")]
        public async Task<IActionResult> Import(int id, [FromBody] TocImportRequest request)
        {
            var exists = await _db.LegalDocuments.AsNoTracking().AnyAsync(d => d.Id == id);
            if (!exists) return NotFound("Document not found.");

            try
            {
                await _toc.ImportAsync(id, request);
                return Ok(new { message = "Imported." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
