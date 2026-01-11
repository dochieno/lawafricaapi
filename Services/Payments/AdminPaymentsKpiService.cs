using LawAfrica.API.Data;
using LawAfrica.API.Models.DTOs.AdminDashboard;
using LawAfrica.API.Models.Payments;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Payments
{
    public class AdminPaymentsKpiService
    {
        private readonly ApplicationDbContext _db;

        public AdminPaymentsKpiService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<PaymentsKpiResponse> GetAsync(PaymentsKpiRequest req, CancellationToken ct)
        {
            if (req.ToUtc <= req.FromUtc)
                throw new InvalidOperationException("ToUtc must be greater than FromUtc.");

            // Paid invoices in window
            var invQ = _db.Invoices.AsNoTracking().Where(x =>
                x.Status == InvoiceStatus.Paid &&
                x.PaidAt != null &&
                x.PaidAt >= req.FromUtc &&
                x.PaidAt <= req.ToUtc);

            if (req.InstitutionId.HasValue)
                invQ = invQ.Where(x => x.InstitutionId == req.InstitutionId.Value);

            if (req.UserId.HasValue)
                invQ = invQ.Where(x => x.UserId == req.UserId.Value);

            var invoices = await invQ
                .Select(x => new { x.Id, x.Total, x.Purpose })
                .ToListAsync(ct);

            var paidCount = invoices.Count;
            var totalRevenue = invoices.Sum(x => x.Total);
            var avg = paidCount == 0 ? 0m : totalRevenue / paidCount;

            // Revenue by purpose (invoice-level truth)
            var byPurpose = invoices
                .GroupBy(x => x.Purpose)
                .Select(g => new RevenueByPurposeRow
                {
                    Purpose = g.Key,
                    Revenue = g.Sum(x => x.Total),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Revenue)
                .ToList();

            // Revenue by provider:
            // We infer provider by joining Invoice -> PaymentIntent via PaymentIntent.InvoiceId
            var intentQ = _db.PaymentIntents.AsNoTracking()
                .Where(pi => pi.InvoiceId != null);

            if (req.InstitutionId.HasValue)
                intentQ = intentQ.Where(pi => pi.InstitutionId == req.InstitutionId.Value);

            if (req.UserId.HasValue)
                intentQ = intentQ.Where(pi => pi.UserId == req.UserId.Value);

            // Only those intents whose invoice was paid in window
            var paidInvoiceIds = invoices.Select(x => x.Id).ToList();

            var providerRows = await intentQ
                .Where(pi => pi.InvoiceId != null && paidInvoiceIds.Contains(pi.InvoiceId.Value))
                .Join(_db.Invoices.AsNoTracking(),
                    pi => pi.InvoiceId,
                    inv => inv.Id,
                    (pi, inv) => new { pi.Provider, inv.Total })
                .GroupBy(x => x.Provider)
                .Select(g => new RevenueByProviderRow
                {
                    Provider = g.Key,
                    Revenue = g.Sum(x => x.Total),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Revenue)
                .ToListAsync(ct);

            // Reconciliation health: count items in time window
            // We use PaymentReconciliationItems.CreatedAt in the same window.
            var recQ = _db.PaymentReconciliationItems.AsNoTracking()
                .Where(x => x.CreatedAt >= req.FromUtc && x.CreatedAt <= req.ToUtc);

            // Optional filter by institution/user via join to PaymentIntent (nullable)
            if (req.InstitutionId.HasValue || req.UserId.HasValue)
            {
                recQ = recQ.Where(x => x.PaymentIntentId != null);
                recQ = recQ.Join(_db.PaymentIntents.AsNoTracking(),
                        r => r.PaymentIntentId,
                        pi => pi.Id,
                        (r, pi) => new { r, pi })
                    .Where(x =>
                        (!req.InstitutionId.HasValue || x.pi.InstitutionId == req.InstitutionId.Value) &&
                        (!req.UserId.HasValue || x.pi.UserId == req.UserId.Value))
                    .Select(x => x.r);
            }

            var grouped = await recQ
                .GroupBy(x => x.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            int Count(ReconciliationStatus s) => grouped.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

            var health = new ReconciliationHealth
            {
                TotalItems = grouped.Sum(x => x.Count),
                Matched = Count(ReconciliationStatus.Matched),
                NeedsReview = Count(ReconciliationStatus.NeedsReview),
                Mismatch = Count(ReconciliationStatus.Mismatch),
                MissingInternalIntent = Count(ReconciliationStatus.MissingInternalIntent),
                MissingProviderTransaction = Count(ReconciliationStatus.MissingProviderTransaction),
                Duplicate = Count(ReconciliationStatus.Duplicate),
                FinalizerFailed = Count(ReconciliationStatus.FinalizerFailed),
                ManuallyResolved = Count(ReconciliationStatus.ManuallyResolved)
            };

            health.Score = ComputeHealthScore(health);
            health.Summary = BuildHealthSummary(health);

            return new PaymentsKpiResponse
            {
                FromUtc = req.FromUtc,
                ToUtc = req.ToUtc,
                TotalRevenue = totalRevenue,
                PaidInvoices = paidCount,
                AverageInvoiceValue = avg,
                RevenueByProvider = providerRows,
                RevenueByPurpose = byPurpose,
                Health = health
            };
        }

        private static int ComputeHealthScore(ReconciliationHealth h)
        {
            if (h.TotalItems <= 0) return 100;

            // Weighted penalties (tweak anytime)
            var penalty =
                (h.Mismatch * 6) +
                (h.MissingInternalIntent * 8) +
                (h.MissingProviderTransaction * 7) +
                (h.Duplicate * 5) +
                (h.FinalizerFailed * 7) +
                (h.NeedsReview * 2);

            // normalize penalty by total
            var maxPenalty = h.TotalItems * 8; // worst-case per item
            var score = 100 - (int)Math.Round((penalty / (double)maxPenalty) * 100);

            if (score < 0) score = 0;
            if (score > 100) score = 100;

            return score;
        }

        private static string BuildHealthSummary(ReconciliationHealth h)
        {
            if (h.TotalItems <= 0) return "No reconciliation data in this period.";

            if (h.Score >= 95) return "Excellent: reconciliation looks healthy.";
            if (h.Score >= 85) return "Good: minor items need review.";
            if (h.Score >= 70) return "Fair: investigate mismatches and failed finalizations.";
            return "Poor: urgent reconciliation attention required.";
        }
    }
}
