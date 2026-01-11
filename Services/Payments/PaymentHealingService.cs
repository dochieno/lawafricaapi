using LawAfrica.API.Data;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LawAfrica.API.Services.Payments
{
    public class PaymentHealingService
    {
        private readonly ApplicationDbContext _db;
        private readonly PaymentFinalizerService _finalizer;
        private readonly LegalDocumentPurchaseFulfillmentService _legalDocFulfillment;
        private readonly PaymentHealingOptions _opts;
        private readonly ILogger<PaymentHealingService> _logger;

        public PaymentHealingService(
            ApplicationDbContext db,
            PaymentFinalizerService finalizer,
            LegalDocumentPurchaseFulfillmentService legalDocFulfillment,
            IOptions<PaymentHealingOptions> opts,
            ILogger<PaymentHealingService> logger)
        {
            _db = db;
            _finalizer = finalizer;
            _legalDocFulfillment = legalDocFulfillment;
            _opts = opts.Value;
            _logger = logger;
        }

        public async Task<PaymentHealingResult> RunAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var minAge = TimeSpan.FromMinutes(Math.Max(0, _opts.MinAgeMinutes));
            var cutoff = now.Subtract(minAge);

            var result = new PaymentHealingResult();

            // 1) Retry finalizer: Success but not finalized
            var toFinalize = await _db.PaymentIntents
                .Where(x => x.Status == PaymentStatus.Success
                            && x.IsFinalized == false
                            && (x.UpdatedAt ?? x.CreatedAt) <= cutoff)
                .OrderBy(x => x.Id)
                .Take(Math.Max(1, _opts.BatchSize))
                .Select(x => x.Id)
                .ToListAsync(ct);

            foreach (var id in toFinalize)
            {
                try
                {
                    await _finalizer.FinalizeIfNeededAsync(id);
                    result.FinalizerRetried++;
                }
                catch (Exception ex)
                {
                    result.FinalizerFailed++;
                    _logger.LogError(ex, "[HEAL] Finalizer retry failed PaymentIntentId={Id}", id);
                }
            }

            // 2) Retry legal doc fulfillment: paid but missing purchase record
            // We only consider intents that have UserId + LegalDocumentId
            var candidates = await _db.PaymentIntents
                .AsNoTracking()
                .Where(x => x.Status == PaymentStatus.Success
                            && x.Purpose == PaymentPurpose.PublicLegalDocumentPurchase
                            && x.UserId != null
                            && x.LegalDocumentId != null
                            && (x.UpdatedAt ?? x.CreatedAt) <= cutoff)
                .OrderByDescending(x => x.Id)
                .Take(Math.Max(1, _opts.BatchSize))
                .Select(x => new { x.Id, x.UserId, x.LegalDocumentId })
                .ToListAsync(ct);

            foreach (var c in candidates)
            {
                var already = await _db.UserLegalDocumentPurchases
                    .AsNoTracking()
                    .AnyAsync(p => p.UserId == c.UserId && p.LegalDocumentId == c.LegalDocumentId, ct);

                if (already)
                    continue;

                try
                {
                    var intent = await _db.PaymentIntents.FirstOrDefaultAsync(x => x.Id == c.Id, ct);
                    if (intent == null) continue;

                    await _legalDocFulfillment.FulfillAsync(intent);
                    result.LegalDocFulfillmentRetried++;
                }
                catch (Exception ex)
                {
                    result.LegalDocFulfillmentFailed++;
                    _logger.LogError(ex, "[HEAL] Legal doc fulfillment retry failed PaymentIntentId={Id}", c.Id);
                }
            }

            result.RanAtUtc = DateTime.UtcNow;
            return result;
        }
    }

    public class PaymentHealingResult
    {
        public DateTime RanAtUtc { get; set; }

        public int FinalizerRetried { get; set; }
        public int FinalizerFailed { get; set; }

        public int LegalDocFulfillmentRetried { get; set; }
        public int LegalDocFulfillmentFailed { get; set; }
    }
}
