using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/content-products/{productId:int}/documents")]
    [Authorize(Roles = "Admin")]
    public class ContentProductDocumentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public ContentProductDocumentsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // --------------------------------------------------
        // GET: list documents in a product
        // --------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetDocuments(int productId)
        {
            var productExists = await _db.ContentProducts.AnyAsync(p => p.Id == productId);
            if (!productExists)
                return NotFound("Content product not found.");

            var rows = await _db.ContentProductLegalDocuments
                .AsNoTracking()
                .Include(x => x.LegalDocument)
                .Where(x => x.ContentProductId == productId)
                .OrderBy(x => x.SortOrder)
                .ThenByDescending(x => x.CreatedAt)
                .Select(x => new ProductDocumentDto
                {
                    Id = x.Id,
                    ContentProductId = x.ContentProductId,
                    LegalDocumentId = x.LegalDocumentId,
                    SortOrder = x.SortOrder,
                    DocumentTitle = x.LegalDocument.Title,
                    IsPremium = x.LegalDocument.IsPremium,
                    Status = x.LegalDocument.Status.ToString(),
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();

            return Ok(rows);
        }

        // --------------------------------------------------
        // POST: add document to product
        // --------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> AddDocument(
            int productId,
            [FromBody] AddDocumentToProductRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required.");

            if (request.LegalDocumentId <= 0)
                return BadRequest("LegalDocumentId is required.");

            if (request.SortOrder < 0)
                return BadRequest("SortOrder must be 0 or greater.");

            var product = await _db.ContentProducts.FindAsync(productId);
            if (product == null)
                return NotFound("Content product not found.");

            var doc = await _db.LegalDocuments.FindAsync(request.LegalDocumentId);
            if (doc == null)
                return NotFound("Legal document not found.");

            var exists = await _db.ContentProductLegalDocuments.AnyAsync(x =>
                x.ContentProductId == productId &&
                x.LegalDocumentId == request.LegalDocumentId);

            if (exists)
                return BadRequest("This document is already assigned to the product.");

            var row = new ContentProductLegalDocument
            {
                ContentProductId = productId,
                LegalDocumentId = request.LegalDocumentId,
                SortOrder = request.SortOrder,
                CreatedAt = DateTime.UtcNow
            };

            _db.ContentProductLegalDocuments.Add(row);

            // Legacy support (do not overwrite)
            if (doc.ContentProductId == null)
                doc.ContentProductId = productId;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Document added to product.", id = row.Id });
        }

        // --------------------------------------------------
        // PUT: update document sort order
        // --------------------------------------------------
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateMapping(
            int productId,
            int id,
            [FromBody] UpdateProductDocumentRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required.");

            if (request.SortOrder < 0)
                return BadRequest("SortOrder must be 0 or greater.");

            var row = await _db.ContentProductLegalDocuments
                .FirstOrDefaultAsync(x => x.Id == id && x.ContentProductId == productId);

            if (row == null)
                return NotFound("Mapping not found.");

            row.SortOrder = request.SortOrder;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Mapping updated." });
        }

        // --------------------------------------------------
        // DELETE: remove document from product
        // --------------------------------------------------
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> RemoveDocument(int productId, int id)
        {
            var row = await _db.ContentProductLegalDocuments
                .FirstOrDefaultAsync(x => x.Id == id && x.ContentProductId == productId);

            if (row == null)
                return NotFound("Mapping not found.");

            _db.ContentProductLegalDocuments.Remove(row);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Document removed from product." });
        }
    }
}
