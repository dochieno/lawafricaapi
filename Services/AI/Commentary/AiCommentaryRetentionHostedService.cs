using LawAfrica.API.Data;
using LawAfrica.API.Models.Ai.Commentary;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Ai.Commentary
{
    /// <summary>
    /// Purges expired AI commentary threads based on DB setting AiCommentarySettings.RetentionMonths.
    /// Uses LastActivityAtUtc as expiry reference.
    /// </summary>
    public class AiCommentaryRetentionHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AiCommentaryRetentionHostedService> _logger;

        public AiCommentaryRetentionHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<AiCommentaryRetentionHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Run shortly after startup, then daily.
            await SafeRunOnce(stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SafeRunOnce(stoppingToken);
            }
        }

        private async Task SafeRunOnce(CancellationToken ct)
        {
            try
            {
                await RunOnce(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI commentary retention purge failed: {Message}", ex.Message);
            }
        }

        private async Task RunOnce(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Load retention setting (singleton row Id=1)
            var settings = await db.AiCommentarySettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == 1, ct);

            var retentionMonths = settings?.RetentionMonths ?? 6; // fallback only if row missing
            if (retentionMonths < 1) retentionMonths = 1;

            var cutoff = DateTime.UtcNow.AddMonths(-retentionMonths);

            // Safety: purge in batches to avoid long locks.
            const int batchSize = 1000;
            var totalDeleted = 0;

            while (!ct.IsCancellationRequested)
            {
                // Select thread ids eligible for purge
                var ids = await db.AiCommentaryThreads
                    .AsNoTracking()
                    .Where(t => t.LastActivityAtUtc < cutoff)
                    .OrderBy(t => t.LastActivityAtUtc)
                    .Select(t => t.Id)
                    .Take(batchSize)
                    .ToListAsync(ct);

                if (ids.Count == 0) break;

                // Delete threads; DB cascade will delete messages + sources (FK cascade)
                var deleted = await db.AiCommentaryThreads
                    .Where(t => ids.Contains(t.Id))
                    .ExecuteDeleteAsync(ct);

                totalDeleted += deleted;

                // small pause to reduce pressure (optional)
                await Task.Delay(150, ct);
            }

            if (totalDeleted > 0)
            {
                _logger.LogInformation(
                    "AI commentary retention purge deleted {Count} threads older than {Cutoff} (RetentionMonths={Months}).",
                    totalDeleted, cutoff, retentionMonths);
            }
        }
    }
}
