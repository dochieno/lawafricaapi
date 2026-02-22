// =======================================================
// FILE: LawAfrica.API/Controllers/Admin/Lawyers/AdminPracticeAreasController.cs
// =======================================================
using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Lawyers.Admin;
using LawAfrica.API.Models.Lawyers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin.Lawyers
{
    [ApiController]
    [Route("api/admin/lawyers/practice-areas")]
    [Authorize(Roles = "Admin")]
    public class AdminPracticeAreasController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        public AdminPracticeAreasController(ApplicationDbContext db) => _db = db;

        // GET /api/admin/lawyers/practice-areas?q=family&includeInactive=true
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? q = null,
            [FromQuery] bool includeInactive = false,
            [FromQuery] int take = 500,
            CancellationToken ct = default)
        {
            q = (q ?? "").Trim();
            take = Math.Clamp(take, 1, 1000);

            var query = _db.PracticeAreas.AsNoTracking().AsQueryable();

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(x => x.Name.Contains(q) || (x.Slug != null && x.Slug.Contains(q)));

            var items = await query
                .OrderBy(x => x.Name)
                .Take(take)
                .Select(x => new AdminPracticeAreaDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Slug = x.Slug,
                    IsActive = x.IsActive
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        // POST /api/admin/lawyers/practice-areas
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AdminPracticeAreaUpsertDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var name = (dto.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Name is required." });

            var slug = string.IsNullOrWhiteSpace(dto.Slug) ? null : dto.Slug.Trim();

            var dup = await _db.PracticeAreas.AsNoTracking()
                .AnyAsync(x => x.Name == name, ct);

            if (dup)
                return Conflict(new { message = "Practice area already exists with the same name." });

            var pa = new PracticeArea
            {
                Name = name,
                Slug = slug,
                IsActive = dto.IsActive
            };

            _db.PracticeAreas.Add(pa);
            await _db.SaveChangesAsync(ct);

            return Ok(new AdminPracticeAreaDto
            {
                Id = pa.Id,
                Name = pa.Name,
                Slug = pa.Slug,
                IsActive = pa.IsActive
            });
        }

        // PUT /api/admin/lawyers/practice-areas/{id}
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] AdminPracticeAreaUpsertDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var pa = await _db.PracticeAreas.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (pa == null) return NotFound(new { message = "Practice area not found." });

            var name = (dto.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Name is required." });

            var slug = string.IsNullOrWhiteSpace(dto.Slug) ? null : dto.Slug.Trim();

            var dup = await _db.PracticeAreas.AsNoTracking()
                .AnyAsync(x => x.Id != id && x.Name == name, ct);

            if (dup)
                return Conflict(new { message = "Another practice area already uses that name." });

            pa.Name = name;
            pa.Slug = slug;
            pa.IsActive = dto.IsActive;

            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        // DELETE /api/admin/lawyers/practice-areas/{id}
        // Soft delete => IsActive=false
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Disable(int id, CancellationToken ct)
        {
            var pa = await _db.PracticeAreas.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (pa == null) return NotFound(new { message = "Practice area not found." });

            pa.IsActive = false;
            await _db.SaveChangesAsync(ct);

            return NoContent();
        }
    }
}