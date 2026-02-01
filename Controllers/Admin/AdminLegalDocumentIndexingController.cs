using LawAfrica.API.Data;
using LawAfrica.API.Services.Documents.Indexing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/legal-documents")]
    [Authorize(Policy = "RequireAdminOrGlobalAdmin")]
    public sealed class AdminLegalDocumentIndexingController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILegalDocumentIndexingQueue _queue;

        public AdminLegalDocumentIndexingController(ApplicationDbContext db, ILegalDocumentIndexingQueue queue)
        {
            _db = db;
            _queue = queue;
        }

        // ------------------------------------------------------------
        // 1) Index ONE document (best for testing)
        // POST /api/admin/legal-documents/{id}/index-text?force=false
        // ------------------------------------------------------------
        [HttpPost("{id:int}/index-text")]
        public async Task<IActionResult> IndexOne(int id, [FromQuery] bool force = false, CancellationToken ct = default)
        {
            if (id <= 0) return BadRequest(new { message = "Invalid document id." });

            var exists = await _db.LegalDocuments
                .AsNoTracking()
                .AnyAsync(x => x.Id == id, ct);

            if (!exists) return NotFound(new { message = $"Document {id} not found." });

            await _queue.EnqueueAsync(new LegalDocumentIndexJob(id, force), ct);
            return Ok(new { message = "Index job queued.", legalDocumentId = id, force });
        }

        // ------------------------------------------------------------
        // 2) Index SELECTED ids (avoid indexing everything)
        // POST /api/admin/legal-documents/index-text/bulk-by-ids?force=false
        // Body: { "ids": [74, 75, 80] }
        // ------------------------------------------------------------
        public sealed class BulkByIdsRequest
        {
            public List<int> Ids { get; set; } = new();
        }

        [HttpPost("index-text/bulk-by-ids")]
        public async Task<IActionResult> IndexBulkByIds(
            [FromBody] BulkByIdsRequest req,
            [FromQuery] bool force = false,
            CancellationToken ct = default)
        {
            var ids = (req?.Ids ?? new List<int>())
                .Select(x => x)
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return BadRequest(new { message = "Provide at least one valid id.", example = new { ids = new[] { 74, 75 } } });

            // Only queue existing docs (and pdf by default)
            var existingIds = await _db.LegalDocuments
                .AsNoTracking()
                .Where(d => ids.Contains(d.Id) && d.FileType == "pdf")
                .Select(d => d.Id)
                .ToListAsync(ct);

            foreach (var docId in existingIds)
                await _queue.EnqueueAsync(new LegalDocumentIndexJob(docId, force), ct);

            return Ok(new
            {
                message = "Selected index jobs queued.",
                requested = ids.Count,
                queued = existingIds.Count,
                missingOrNotPdf = ids.Except(existingIds).ToList(),
                force
            });
        }

        // ------------------------------------------------------------
        // 3) Controlled batch indexing:
        // - onlyNotIndexed=true => only LastIndexedAt == null
        // - take=10 => only queue 10 docs this run
        // POST /api/admin/legal-documents/index-text/bulk?onlyNotIndexed=true&take=10&force=false
        // ------------------------------------------------------------
        [HttpPost("index-text/bulk")]
        public async Task<IActionResult> IndexBulk(
            [FromQuery] bool onlyNotIndexed = true,
            [FromQuery] int take = 10,
            [FromQuery] bool force = false,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 200); // safety

            var q = _db.LegalDocuments.AsNoTracking().Where(d => d.FileType == "pdf");

            if (onlyNotIndexed && !force)
                q = q.Where(d => d.LastIndexedAt == null);

            var ids = await q
                .OrderBy(d => d.Id)
                .Select(d => d.Id)
                .Take(take)
                .ToListAsync(ct);

            foreach (var docId in ids)
                await _queue.EnqueueAsync(new LegalDocumentIndexJob(docId, force), ct);

            return Ok(new
            {
                message = "Batch index jobs queued.",
                queued = ids.Count,
                take,
                onlyNotIndexed,
                force
            });
        }

        // ------------------------------------------------------------
        // Status: how many page rows exist
        // GET /api/admin/legal-documents/{id}/index-text/status
        // ------------------------------------------------------------
        [HttpGet("{id:int}/index-text/status")]
        public async Task<IActionResult> Status(int id, CancellationToken ct = default)
        {
            if (id <= 0) return BadRequest(new { message = "Invalid document id." });

            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.FileType,
                    x.FilePath,
                    x.PageCount,
                    x.LastIndexedAt
                })
                .FirstOrDefaultAsync(ct);

            if (doc == null) return NotFound(new { message = "Document not found." });

            var rows = await _db.LegalDocumentPageTexts
                .AsNoTracking()
                .Where(x => x.LegalDocumentId == id)
                .CountAsync(ct);

            return Ok(new
            {
                legalDocumentId = doc.Id,
                fileType = doc.FileType,
                pageCount = doc.PageCount,
                lastIndexedAt = doc.LastIndexedAt,
                indexedPages = rows
            });
        }
    }
}
