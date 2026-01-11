using LawAfrica.API.Data;
using LawAfrica.API.Models.DTOs.Institutions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/public/institutions")]
    [AllowAnonymous]
    public class PublicInstitutionsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public PublicInstitutionsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET: /api/public/institutions?q=foo
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? q = null)
        {
            q = (q ?? "").Trim().ToLowerInvariant();

            var query = _db.Institutions.AsNoTracking()
                .Where(i => i.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(i =>
                    i.Name.ToLower().Contains(q) ||
                    (i.ShortName != null && i.ShortName.ToLower().Contains(q)) ||
                    i.EmailDomain.ToLower().Contains(q)
                );
            }

            var items = await query
                .OrderBy(i => i.Name)
                .Select(i => new PublicInstitutionDto
                {
                    Id = i.Id,
                    Name = i.Name,
                    EmailDomain = i.EmailDomain,
                    RequiresAccessCode = !string.IsNullOrWhiteSpace(i.InstitutionAccessCode)
                })
                .ToListAsync();

            return Ok(items);
        }
    }
}
