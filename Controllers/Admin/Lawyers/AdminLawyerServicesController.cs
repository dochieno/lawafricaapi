// =======================================================
// FILE: LawAfrica.API/Controllers/Admin/Lawyers/AdminLawyerServicesController.cs
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
    [Route("api/admin/lawyers/services")]
    [Authorize(Roles = "Admin")]
    public class AdminLawyerServicesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        public AdminLawyerServicesController(ApplicationDbContext db) => _db = db;

        // GET /api/admin/lawyers/services?q=consult&includeInactive=true
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? q = null,
            [FromQuery] bool includeInactive = false,
            [FromQuery] int take = 500,
            CancellationToken ct = default)
        {
            q = (q ?? "").Trim();
            take = Math.Clamp(take, 1, 1000);

            var query = _db.LawyerServices.AsNoTracking().AsQueryable();

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(x => x.Name.Contains(q) || (x.Slug != null && x.Slug.Contains(q)));

            var items = await query
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Take(take)
                .Select(x => new AdminLawyerServiceDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Slug = x.Slug,
                    SortOrder = x.SortOrder,
                    IsActive = x.IsActive
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        // POST /api/admin/lawyers/services
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AdminLawyerServiceUpsertDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var name = (dto.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Name is required." });

            var slug = string.IsNullOrWhiteSpace(dto.Slug) ? null : dto.Slug.Trim();

            var dup = await _db.LawyerServices.AsNoTracking()
                .AnyAsync(x => x.Name == name, ct);

            if (dup)
                return Conflict(new { message = "Service already exists with the same name." });

            var s = new LawyerService
            {
                Name = name,
                Slug = slug,
                SortOrder = dto.SortOrder,
                IsActive = dto.IsActive
            };

            _db.LawyerServices.Add(s);
            await _db.SaveChangesAsync(ct);

            return Ok(new AdminLawyerServiceDto
            {
                Id = s.Id,
                Name = s.Name,
                Slug = s.Slug,
                SortOrder = s.SortOrder,
                IsActive = s.IsActive
            });
        }

        // PUT /api/admin/lawyers/services/{id}
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] AdminLawyerServiceUpsertDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var s = await _db.LawyerServices.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s == null) return NotFound(new { message = "Service not found." });

            var name = (dto.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Name is required." });

            var slug = string.IsNullOrWhiteSpace(dto.Slug) ? null : dto.Slug.Trim();

            var dup = await _db.LawyerServices.AsNoTracking()
                .AnyAsync(x => x.Id != id && x.Name == name, ct);

            if (dup)
                return Conflict(new { message = "Another service already uses that name." });

            s.Name = name;
            s.Slug = slug;
            s.SortOrder = dto.SortOrder;
            s.IsActive = dto.IsActive;

            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        // DELETE /api/admin/lawyers/services/{id}
        // Soft delete => IsActive=false
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Disable(int id, CancellationToken ct)
        {
            var s = await _db.LawyerServices.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s == null) return NotFound(new { message = "Service not found." });

            s.IsActive = false;
            await _db.SaveChangesAsync(ct);

            return NoContent();
        }
    }
}