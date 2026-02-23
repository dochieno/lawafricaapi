using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/lookups")]
    [Authorize] // ✅ mandatory: only authorized controllers
    public class LookupsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<LookupsController> _logger;

        public LookupsController(ApplicationDbContext db, ILogger<LookupsController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ✅ Categories lookup (from CategoryMeta)
        // NOTE: Categories are shared across kinds, but your UI will apply them only to Standard docs.
        // GET /api/lookups/legal-document-categories
        [HttpGet("legal-document-categories")]
        public async Task<IActionResult> GetLegalDocumentCategories()
        {
            var items = await _db.LegalDocumentCategoryMetas
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    code = x.Code,
                    name = x.Name,
                    sortOrder = x.SortOrder
                })
                .ToListAsync();

            return Ok(new { items });
        }

        // ✅ SubCategories lookup
        // GET /api/lookups/legal-document-subcategories?categoryId=5
        // GET /api/lookups/legal-document-subcategories?category=Statutes
        [HttpGet("legal-document-subcategories")]
        public async Task<IActionResult> GetLegalDocumentSubCategories(
            [FromQuery] int? categoryId = null,
            [FromQuery] string? category = null)
        {
            // Resolve category filter (optional)
            int? catInt = null;

            if (categoryId.HasValue)
            {
                catInt = categoryId.Value;
            }
            else if (!string.IsNullOrWhiteSpace(category))
            {
                // Parse enum by name (case-insensitive)
                if (Enum.TryParse<LegalDocumentCategory>(category.Trim(), ignoreCase: true, out var parsed))
                    catInt = (int)parsed;
                else
                    return BadRequest("Invalid category. Use a valid LegalDocumentCategory name (e.g. Statutes) or provide categoryId.");
            }

            var q = _db.LegalDocumentSubCategories
                .AsNoTracking()
                .Where(x => x.IsActive);

            if (catInt.HasValue)
                q = q.Where(x => (int)x.Category == catInt.Value);

            var items = await q
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    categoryId = (int)x.Category,
                    code = x.Code,
                    name = x.Name,
                    sortOrder = x.SortOrder
                })
                .ToListAsync();

            return Ok(new { items });
        }

        // ✅ Standard-doc-only categories (optional helper)
        // This excludes categories that currently only exist as Report docs (or have no Standard docs at all).
        // GET /api/lookups/legal-document-categories/standard-only
        [HttpGet("legal-document-categories/standard-only")]
        public async Task<IActionResult> GetLegalDocumentCategoriesStandardOnly()
        {
            // Categories that actually have Standard docs (Published or any status? choose Published for Explore)
            var standardCategoryIds = await _db.LegalDocuments
                .AsNoTracking()
                .Where(d => d.Kind == LawAfrica.API.Models.LawReports.Enums.LegalDocumentKind.Standard)
                .Select(d => (int)d.Category)
                .Distinct()
                .ToListAsync();

            var items = await _db.LegalDocumentCategoryMetas
                .AsNoTracking()
                .Where(x => x.IsActive && standardCategoryIds.Contains(x.Id))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    code = x.Code,
                    name = x.Name,
                    sortOrder = x.SortOrder
                })
                .ToListAsync();

            return Ok(new { items });
        }

        // ✅ Standard-doc-only subcategories (optional helper)
        // GET /api/lookups/legal-document-subcategories/standard-only?categoryId=5
        [HttpGet("legal-document-subcategories/standard-only")]
        public async Task<IActionResult> GetLegalDocumentSubCategoriesStandardOnly(
            [FromQuery] int? categoryId = null,
            [FromQuery] string? category = null)
        {
            int? catInt = null;

            if (categoryId.HasValue)
            {
                catInt = categoryId.Value;
            }
            else if (!string.IsNullOrWhiteSpace(category))
            {
                if (Enum.TryParse<LegalDocumentCategory>(category.Trim(), ignoreCase: true, out var parsed))
                    catInt = (int)parsed;
                else
                    return BadRequest("Invalid category. Use a valid LegalDocumentCategory name (e.g. Statutes) or provide categoryId.");
            }

            // Subcategories that actually appear on Standard docs
            var usedSubCategoryIdsQuery = _db.LegalDocuments
                .AsNoTracking()
                .Where(d => d.Kind == LawAfrica.API.Models.LawReports.Enums.LegalDocumentKind.Standard && d.SubCategoryId != null);

            if (catInt.HasValue)
                usedSubCategoryIdsQuery = usedSubCategoryIdsQuery.Where(d => (int)d.Category == catInt.Value);

            var usedSubCategoryIds = await usedSubCategoryIdsQuery
                .Select(d => d.SubCategoryId!.Value)
                .Distinct()
                .ToListAsync();

            var q = _db.LegalDocumentSubCategories
                .AsNoTracking()
                .Where(x => x.IsActive && usedSubCategoryIds.Contains(x.Id));

            if (catInt.HasValue)
                q = q.Where(x => (int)x.Category == catInt.Value);

            var items = await q
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    categoryId = (int)x.Category,
                    code = x.Code,
                    name = x.Name,
                    sortOrder = x.SortOrder
                })
                .ToListAsync();

            return Ok(new { items });
        }
    }
}