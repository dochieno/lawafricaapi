using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LawAfrica.API.Data;
using LawAfrica.API.Models.Usage;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Usage
{
    /// <summary>
    /// Logs usage events with server-side dedupe (prevents spam).
    /// </summary>
    public class UsageEventLogger
    {
        private readonly ApplicationDbContext _db;

        public UsageEventLogger(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Log an event but only once per (UserId, DocId, Surface) within throttleWindowSeconds.
        /// This protects performance if the frontend spams calls.
        /// </summary>
        public async Task LogOnceAsync(
            int userId,
            int? institutionId,
            int legalDocumentId,
            bool allowed,
            string decisionReason,
            string surface,
            string? ipAddress,
            string? userAgent,
            int throttleWindowSeconds = 180,
            CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddSeconds(-Math.Abs(throttleWindowSeconds));

            // If we logged this recently, skip
            var recentlyLogged = await _db.UsageEvents
                .AsNoTracking()
                .AnyAsync(e =>
                    e.UserId == userId &&
                    e.LegalDocumentId == legalDocumentId &&
                    e.Surface == surface &&
                    e.AtUtc >= windowStart,
                    ct);

            if (recentlyLogged)
                return;

            var ev = new UsageEvent
            {
                AtUtc = now,
                UserId = userId,
                InstitutionId = institutionId,
                LegalDocumentId = legalDocumentId,
                Allowed = allowed,
                DecisionReason = decisionReason ?? "Unknown",
                Surface = surface ?? "Unknown",
                IpAddress = ipAddress ?? "",
                UserAgent = userAgent ?? ""
            };

            _db.UsageEvents.Add(ev);
            await _db.SaveChangesAsync(ct);
        }
    }
}
