using System;
using System.Threading.Tasks;
using LawAfrica.API.Data;
using LawAfrica.API.Models.Usage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Usage
{
    public class UsageEventWriter : IUsageEventWriter
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpContextAccessor _http;

        public UsageEventWriter(ApplicationDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        public async Task LogLegalDocumentAccessAsync(
            int legalDocumentId,
            bool allowed,
            string reason,
            string surface = "ReaderOpen",
            int? userId = null,
            int? institutionId = null)
        {
            // Never throw from analytics logging; do not break main flow.
            try
            {
                var ctx = _http.HttpContext;

                var ip = ctx?.Connection?.RemoteIpAddress?.ToString() ?? "";
                var ua = ctx?.Request?.Headers["User-Agent"].ToString() ?? "";

                var ev = new UsageEvent
                {
                    AtUtc = DateTime.UtcNow,
                    LegalDocumentId = legalDocumentId,
                    Allowed = allowed,
                    DecisionReason = string.IsNullOrWhiteSpace(reason) ? (allowed ? "ALLOWED" : "DENIED") : reason.Trim(),
                    Surface = string.IsNullOrWhiteSpace(surface) ? "ReaderOpen" : surface.Trim(),
                    IpAddress = ip.Length > 64 ? ip[..64] : ip,
                    UserAgent = ua.Length > 400 ? ua[..400] : ua,
                    UserId = userId,
                    InstitutionId = institutionId
                };

                _db.UsageEvents.Add(ev);
                await _db.SaveChangesAsync();
            }
            catch
            {
                // swallow on purpose (analytics must never take down access)
            }
        }
    }
}
