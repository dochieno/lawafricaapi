using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/presence")]
    public class PresenceController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public PresenceController(ApplicationDbContext db)
        {
            _db = db;
        }

        // POST /api/presence/ping
        // Frontend calls every 60s while app is open
        [Authorize]
        [HttpPost("ping")]
        public async Task<IActionResult> Ping()
        {
            var userIdStr =
                User.FindFirstValue("userId") ??
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!int.TryParse(userIdStr, out var userId) || userId <= 0)
                return Unauthorized("Invalid user.");

            var now = DateTime.UtcNow;

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var ua = Request.Headers.UserAgent.ToString();

            var presence = await _db.UserPresences.FirstOrDefaultAsync(p => p.UserId == userId);

            if (presence == null)
            {
                _db.UserPresences.Add(new UserPresence
                {
                    UserId = userId,
                    LastSeenAtUtc = now,
                    LastSeenIp = ip,
                    LastSeenUserAgent = ua
                });
            }
            else
            {
                presence.LastSeenAtUtc = now;
                presence.LastSeenIp = ip;
                presence.LastSeenUserAgent = ua;
            }

            await _db.SaveChangesAsync();

            return Ok(new { ok = true, serverTimeUtc = now });
        }
    }
}
