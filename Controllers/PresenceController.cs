using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PresenceController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public PresenceController(ApplicationDbContext db)
        {
            _db = db;
        }

        // POST /api/presence/ping
        [Authorize]
        [HttpPost("ping")]
        public async Task<IActionResult> Ping()
        {
            var userId = User.GetUserId();
            var now = DateTime.UtcNow;

            var row = await _db.UserPresences.FirstOrDefaultAsync(x => x.UserId == userId);
            if (row == null)
            {
                row = new UserPresence { UserId = userId, LastSeenAtUtc = now };
                _db.UserPresences.Add(row);
            }
            else
            {
                row.LastSeenAtUtc = now;
            }

            await _db.SaveChangesAsync();
            return Ok(new { ok = true, lastSeenAtUtc = now });
        }
    }
}
