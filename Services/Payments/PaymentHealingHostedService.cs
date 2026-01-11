using LawAfrica.API.Models.Payments;
using Microsoft.Extensions.Options;

namespace LawAfrica.API.Services.Payments
{
    public class PaymentHealingHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly PaymentHealingSchedulerOptions _opts;
        private readonly ILogger<PaymentHealingHostedService> _logger;

        // Prevent overlapping runs in a single app instance
        private static int _running = 0;

        public PaymentHealingHostedService(
            IServiceScopeFactory scopeFactory,
            IOptions<PaymentHealingSchedulerOptions> opts,
            ILogger<PaymentHealingHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _opts = opts.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_opts.Enabled)
            {
                _logger.LogInformation("[HEAL-SCHED] Payment healing scheduler is disabled.");
                return;
            }

            var initialDelay = TimeSpan.FromSeconds(Math.Max(0, _opts.InitialDelaySeconds));
            if (initialDelay > TimeSpan.Zero)
            {
                _logger.LogInformation("[HEAL-SCHED] Initial delay {DelaySeconds}s", initialDelay.TotalSeconds);
                await Task.Delay(initialDelay, stoppingToken);
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, _opts.IntervalMinutes));
            _logger.LogInformation("[HEAL-SCHED] Started. Interval={IntervalMinutes} minutes", interval.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Single-instance overlap protection
                    if (Interlocked.Exchange(ref _running, 1) == 1)
                    {
                        _logger.LogWarning("[HEAL-SCHED] Previous run still in progress. Skipping this tick.");
                    }
                    else
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var svc = scope.ServiceProvider.GetRequiredService<PaymentHealingService>();

                        var res = await svc.RunAsync(stoppingToken);

                        _logger.LogInformation(
                            "[HEAL-SCHED] Done. FinalizerRetried={FR} FinalizerFailed={FF} LegalRetried={LR} LegalFailed={LF}",
                            res.FinalizerRetried, res.FinalizerFailed, res.LegalDocFulfillmentRetried, res.LegalDocFulfillmentFailed);
                    }
                }
                catch (OperationCanceledException)
                {
                    // normal shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[HEAL-SCHED] Run failed");
                }
                finally
                {
                    Interlocked.Exchange(ref _running, 0);
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // shutdown
                }
            }

            _logger.LogInformation("[HEAL-SCHED] Stopped.");
        }
    }
}
