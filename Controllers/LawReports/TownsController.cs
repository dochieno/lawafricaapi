using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Locations;
using LawAfrica.API.Models.Locations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/towns")]
    public class TownsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        public TownsController(ApplicationDbContext db) => _db = db;

        // -------------------------
        // PUBLIC: list towns (for dropdown)
        // GET /api/towns?countryId=1&q=kak&take=200
        // -------------------------
        [HttpGet]
        public async Task<ActionResult<List<TownDto>>> List([FromQuery] int countryId, [FromQuery] string? q = null, [FromQuery] int take = 200)
        {
            if (countryId <= 0) return BadRequest(new { message = "countryId is required." });

            q = (q ?? "").Trim();
            take = Math.Clamp(take, 1, 500);

            var query = _db.Towns.AsNoTracking().Where(x => x.CountryId == countryId);

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(x =>
                    x.Name.Contains(q) ||
                    x.PostCode.Contains(q)
                );
            }

            var items = await query
                .OrderBy(x => x.Name)
                .ThenBy(x => x.PostCode)
                .Take(take)
                .Select(x => new TownDto
                {
                    Id = x.Id,
                    CountryId = x.CountryId,
                    PostCode = x.PostCode,
                    Name = x.Name
                })
                .ToListAsync();

            return Ok(items);
        }

        // -------------------------
        // PUBLIC: resolve by postcode (useful for import)
        // GET /api/towns/resolve?countryId=1&postCode=50100
        // -------------------------
        [HttpGet("resolve")]
        public async Task<ActionResult<TownDto>> Resolve([FromQuery] int countryId, [FromQuery] string postCode)
        {
            if (countryId <= 0) return BadRequest(new { message = "countryId is required." });

            var pc = (postCode ?? "").Trim();
            if (string.IsNullOrWhiteSpace(pc)) return BadRequest(new { message = "postCode is required." });

            var t = await _db.Towns.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CountryId == countryId && x.PostCode == pc);

            if (t == null) return NotFound(new { message = "Town not found for that post code." });

            return Ok(new TownDto
            {
                Id = t.Id,
                CountryId = t.CountryId,
                PostCode = t.PostCode,
                Name = t.Name
            });
        }

        // -------------------------
        // ADMIN CRUD
        // -------------------------
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<ActionResult<TownDto>> Create([FromBody] TownUpsertDto dto)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var pc = dto.PostCode.Trim();
            var name = dto.Name.Trim();

            // pre-check for friendlier message (DB index still enforces)
            var dup = await _db.Towns.AnyAsync(x =>
                x.CountryId == dto.CountryId &&
                (x.PostCode == pc || x.Name == name));

            if (dup) return Conflict(new { message = "Town already exists (same Country + PostCode or Name)." });

            var t = new Town
            {
                CountryId = dto.CountryId,
                PostCode = pc,
                Name = name,
                CreatedAt = DateTime.UtcNow
            };

            _db.Towns.Add(t);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(List), new { countryId = t.CountryId }, new TownDto
            {
                Id = t.Id,
                CountryId = t.CountryId,
                PostCode = t.PostCode,
                Name = t.Name
            });
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] TownUpsertDto dto)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var t = await _db.Towns.FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound();

            var pc = dto.PostCode.Trim();
            var name = dto.Name.Trim();

            var dup = await _db.Towns.AnyAsync(x =>
                x.Id != id &&
                x.CountryId == dto.CountryId &&
                (x.PostCode == pc || x.Name == name));

            if (dup) return Conflict(new { message = "Town already exists (same Country + PostCode or Name)." });

            t.CountryId = dto.CountryId;
            t.PostCode = pc;
            t.Name = name;
            t.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var t = await _db.Towns.FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound();

            _db.Towns.Remove(t);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
