using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/legal-document-taxonomy")]
    [Authorize(Roles = "Admin")] // ✅ admin-only
    public class AdminLegalDocumentTaxonomyController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AdminLegalDocumentTaxonomyController> _logger;

        public AdminLegalDocumentTaxonomyController(ApplicationDbContext db, ILogger<AdminLegalDocumentTaxonomyController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // =========================================================
        // Categories (Meta) — ONLY existing enum ids are allowed
        // =========================================================

        // GET /api/admin/legal-document-taxonomy/categories
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var items = await _db.LegalDocumentCategoryMetas
                .AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    code = x.Code,
                    name = x.Name,
                    description = x.Description,
                    sortOrder = x.SortOrder,
                    isActive = x.IsActive
                })
                .ToListAsync();

            return Ok(new { items });
        }

        // PUT /api/admin/legal-document-taxonomy/categories/{id}
        // Updates only (no create/delete) to avoid messing with enum mapping
        [HttpPut("categories/{id:int}")]
        public async Task<IActionResult> UpdateCategory(int id, [FromBody] UpdateCategoryMetaRequest request)
        {
            var row = await _db.LegalDocumentCategoryMetas.FirstOrDefaultAsync(x => x.Id == id);
            if (row == null)
                return NotFound("Category meta not found.");

            row.Code = (request.Code ?? "").Trim();
            row.Name = (request.Name ?? "").Trim();
            row.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            row.SortOrder = request.SortOrder;
            row.IsActive = request.IsActive;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Category updated successfully.", id = row.Id });
        }

        public class UpdateCategoryMetaRequest
        {
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public string? Description { get; set; }
            public int SortOrder { get; set; } = 0;
            public bool IsActive { get; set; } = true;
        }

        // =========================================================
        // SubCategories
        // =========================================================

        // GET /api/admin/legal-document-taxonomy/subcategories?categoryId=5
        [HttpGet("subcategories")]
        public async Task<IActionResult> GetSubCategories([FromQuery] int? categoryId = null)
        {
            var q = _db.LegalDocumentSubCategories.AsNoTracking();

            if (categoryId.HasValue)
                q = q.Where(x => (int)x.Category == categoryId.Value);

            var items = await q
                .OrderBy(x => (int)x.Category)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    categoryId = (int)x.Category,
                    code = x.Code,
                    name = x.Name,
                    sortOrder = x.SortOrder,
                    isActive = x.IsActive,
                    countryId = x.CountryId
                })
                .ToListAsync();

            return Ok(new { items });
        }

        // POST /api/admin/legal-document-taxonomy/subcategories
        [HttpPost("subcategories")]
        public async Task<IActionResult> CreateSubCategory([FromBody] UpsertSubCategoryRequest request)
        {
            // ✅ We only care about Standard document taxonomy.
            // CategoryId here maps to LegalDocumentCategory enum (not Kind).
            if (request.CategoryId <= 0)
                return BadRequest("CategoryId is required.");

            if (!Enum.IsDefined(typeof(LegalDocumentCategory), request.CategoryId))
                return BadRequest("Invalid CategoryId (must map to LegalDocumentCategory enum).");

            var catEnum = (LegalDocumentCategory)request.CategoryId;

            var code = (request.Code ?? "").Trim();
            var name = (request.Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                return BadRequest("Code and Name are required.");

            var exists = await _db.LegalDocumentSubCategories.AnyAsync(x =>
                (int)x.Category == request.CategoryId && x.Code == code);

            if (exists)
                return BadRequest("A subcategory with this code already exists under the selected category.");

            var row = new LegalDocumentSubCategory
            {
                Category = catEnum,
                Code = code,
                Name = name,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive,
                CountryId = request.CountryId
            };

            _db.LegalDocumentSubCategories.Add(row);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Subcategory created successfully.", id = row.Id });
        }

        // PUT /api/admin/legal-document-taxonomy/subcategories/{id}
        [HttpPut("subcategories/{id:int}")]
        public async Task<IActionResult> UpdateSubCategory(int id, [FromBody] UpsertSubCategoryRequest request)
        {
            var row = await _db.LegalDocumentSubCategories.FirstOrDefaultAsync(x => x.Id == id);
            if (row == null)
                return NotFound("Subcategory not found.");

            if (request.CategoryId <= 0)
                return BadRequest("CategoryId is required.");

            if (!Enum.IsDefined(typeof(LegalDocumentCategory), request.CategoryId))
                return BadRequest("Invalid CategoryId (must map to LegalDocumentCategory enum).");

            var catEnum = (LegalDocumentCategory)request.CategoryId;

            var code = (request.Code ?? "").Trim();
            var name = (request.Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                return BadRequest("Code and Name are required.");

            // uniqueness per category
            var exists = await _db.LegalDocumentSubCategories.AnyAsync(x =>
                x.Id != id && (int)x.Category == request.CategoryId && x.Code == code);

            if (exists)
                return BadRequest("A subcategory with this code already exists under the selected category.");

            row.Category = catEnum;
            row.Code = code;
            row.Name = name;
            row.SortOrder = request.SortOrder;
            row.IsActive = request.IsActive;
            row.CountryId = request.CountryId;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Subcategory updated successfully.", id = row.Id });
        }

        // DELETE /api/admin/legal-document-taxonomy/subcategories/{id}
        // Soft delete via IsActive=false to avoid breaking existing documents
        [HttpDelete("subcategories/{id:int}")]
        public async Task<IActionResult> DisableSubCategory(int id)
        {
            var row = await _db.LegalDocumentSubCategories.FirstOrDefaultAsync(x => x.Id == id);
            if (row == null)
                return NotFound("Subcategory not found.");

            row.IsActive = false;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Subcategory disabled successfully.", id });
        }

        public class UpsertSubCategoryRequest
        {
            public int CategoryId { get; set; } // maps to LegalDocumentCategory enum
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public int SortOrder { get; set; } = 0;
            public bool IsActive { get; set; } = true;

            public int? CountryId { get; set; } // optional future scoping
        }
    }
}