using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services;

public interface ISubscriptionReconciler
{
    Task<ReconcileResult> ReconcileAsync(CancellationToken ct);
}

public sealed record ReconcileResult(int ExpiredCount, DateTime NowUtc);

public sealed class SubscriptionExpiryReconciler : BackgroundService, ISubscriptionReconciler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionExpiryReconciler> _logger;

    // Run daily at 00:10 UTC (adjust)
    private static readonly TimeSpan RunAtUtc = new(0, 10, 0);

    public SubscriptionExpiryReconciler(IServiceScopeFactory scopeFactory, ILogger<SubscriptionExpiryReconciler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DelayUntilNextRunUtc(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var res = await ReconcileAsync(stoppingToken);
                if (res.ExpiredCount > 0)
                    _logger.LogInformation("Expired {Count} subscription(s) at {Now}.", res.ExpiredCount, res.NowUtc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subscription expiry reconciliation failed.");
            }

            // Wait ~24h
            try { await Task.Delay(TimeSpan.FromDays(1), stoppingToken); }
            catch (TaskCanceledException) { }
        }
    }

    public async Task<ReconcileResult> ReconcileAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;

        // Mark overdue Active subscriptions as Expired (inactive)
        var expiredCount = await db.UserProductSubscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.EndDate < now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, SubscriptionStatus.Expired), ct);

        return new ReconcileResult(expiredCount, now);
    }

    private static async Task DelayUntilNextRunUtc(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var todayRun = now.Date + RunAtUtc;
        var nextRun = now <= todayRun ? todayRun : todayRun.AddDays(1);
        var delay = nextRun - now;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        await Task.Delay(delay, ct);
    }
}