using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CountryController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public CountryController(ApplicationDbContext db)
        {
            _db = db;
        }

        // ✅ Any authenticated OR unauthenticated user? You choose:

        // Option 1: public (no auth)
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var countries = await _db.Countries
                                     .OrderBy(c => c.Name)
                                     .ToListAsync();
            return Ok(countries);
        }

        // 🔒 Admin-only
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Country model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            _db.Countries.Add(model);
            await _db.SaveChangesAsync();
            return Ok(model);
        }
    }

}
