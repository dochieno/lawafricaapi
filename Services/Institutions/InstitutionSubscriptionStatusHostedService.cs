using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services
{
    /// <summary>
    /// ✅ Phase 1: Background job to auto-transition subscription statuses safely:
    /// Pending → Active → Expired
    /// - Never touches Suspended
    /// - Writes audit entries (AutoStatusChanged)
    ///
    /// IMPORTANT:
    /// EF Core cannot translate custom methods (DeriveStatus) into SQL,
    /// so we only use SQL-translatable filters in queries, then compute DeriveStatus in memory.
    /// </summary>
    public class InstitutionSubscriptionStatusHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InstitutionSubscriptionStatusHostedService> _logger;

        // Run interval (safe default). You can later move to appsettings.
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        public InstitutionSubscriptionStatusHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<InstitutionSubscriptionStatusHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("InstitutionSubscriptionStatusHostedService started.");

            // Small initial delay so app can finish booting
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnce(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // normal shutdown
                }
                catch (Exception ex)
                {
                    // Keep API alive (Swagger etc.)
                    _logger.LogError(ex, "Error in subscription status hosted service.");
                }

                await Task.Delay(Interval, stoppingToken);
            }

            _logger.LogInformation("InstitutionSubscriptionStatusHostedService stopped.");
        }

        private async Task RunOnce(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var now = DateTime.UtcNow;

            const int batchSize = 500;
            int totalUpdated = 0;

            int lastId = 0;

            while (!ct.IsCancellationRequested)
            {
                // ✅ SQL-only candidate detection (NO DeriveStatus here)
                // We fetch subscriptions that are likely to be "wrong" given now.
                // Then we compute DeriveStatus in memory and only update if needed.
                var batch = await db.InstitutionProductSubscriptions
                    .Where(s => s.Id > lastId)
                    .Where(s => s.Status != SubscriptionStatus.Suspended)
                    .Where(s =>
                        // Pending that should now be Active/Expired
                        (s.Status == SubscriptionStatus.Pending && s.StartDate <= now) ||

                        // Active that should now be Expired, or Active but not yet started (data issue)
                        (s.Status == SubscriptionStatus.Active && (s.EndDate <= now || s.StartDate > now)) ||

                        // Expired that should now be Active (endDate in future), or Expired but not yet started (data issue)
                        (s.Status == SubscriptionStatus.Expired && (s.EndDate > now || s.StartDate > now))
                    )
                    .OrderBy(s => s.Id)
                    .Take(batchSize)
                    .ToListAsync(ct);

                if (batch.Count == 0) break;

                // advance pagination cursor safely
                lastId = batch[^1].Id;

                foreach (var sub in batch)
                {
                    // Defense in depth
                    if (sub.Status == SubscriptionStatus.Suspended) continue;

                    var derived = DeriveStatus(sub.StartDate, sub.EndDate, now);
                    if (derived == sub.Status) continue;

                    var oldStart = sub.StartDate;
                    var oldEnd = sub.EndDate;
                    var oldStatus = sub.Status;

                    sub.Status = derived;

                    db.InstitutionSubscriptionAudits.Add(new InstitutionSubscriptionAudit
                    {
                        SubscriptionId = sub.Id,
                        Action = SubscriptionAuditAction.AutoStatusChanged,
                        PerformedByUserId = null,

                        OldStartDate = oldStart,
                        OldEndDate = oldEnd,
                        OldStatus = oldStatus,

                        NewStartDate = sub.StartDate,
                        NewEndDate = sub.EndDate,
                        NewStatus = sub.Status,

                        Notes = $"Auto transition by hosted service at {now:O}.",
                        CreatedAt = now
                    });

                    totalUpdated++;
                }

                await db.SaveChangesAsync(ct);

                // If we got fewer than batchSize, we're done
                if (batch.Count < batchSize) break;
            }

            if (totalUpdated > 0)
                _logger.LogInformation("Auto-status transition updated {Count} subscription(s).", totalUpdated);
        }

        private static SubscriptionStatus DeriveStatus(DateTime startDate, DateTime endDate, DateTime now)
        {
            if (startDate > now) return SubscriptionStatus.Pending;
            if (endDate <= now) return SubscriptionStatus.Expired;
            return SubscriptionStatus.Active;
        }
    }
}
