using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Admin.Pricing;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/content-products")]
    [Authorize(Roles = "Admin")] // ✅ NEW: admin-only pricing management
    public class GlobalAdminContentProductPricesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public GlobalAdminContentProductPricesController(ApplicationDbContext db)
        {
            _db = db;
        }

        // ------------------------------------------------------------
        // GET: /api/admin/content-products
        // Used by admin UI dropdown
        // ------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetContentProducts(CancellationToken ct)
        {
            var items = await _db.ContentProducts
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        // ------------------------------------------------------------
        // GET: /api/admin/content-products/{contentProductId}/prices
        // ------------------------------------------------------------
        [HttpGet("{contentProductId:int}/prices")]
        public async Task<IActionResult> GetPrices(int contentProductId, CancellationToken ct)
        {
            var exists = await _db.ContentProducts
                .AsNoTracking()
                .AnyAsync(x => x.Id == contentProductId, ct);

            if (!exists)
                return NotFound(new { message = "Content product not found." });

            var prices = await _db.ContentProductPrices
                .AsNoTracking()
                .Where(p => p.ContentProductId == contentProductId)
                .OrderByDescending(p => p.IsActive)
                .ThenBy(p => p.Audience)
                .ThenBy(p => p.BillingPeriod)
                .ThenBy(p => p.Currency)
                .ThenByDescending(p => p.EffectiveFromUtc)
                .Select(p => new
                {
                    p.Id,
                    p.ContentProductId,
                    p.Audience,
                    p.BillingPeriod,
                    p.Currency,
                    p.Amount,
                    p.IsActive,
                    p.EffectiveFromUtc,
                    p.EffectiveToUtc,
                    p.CreatedAtUtc
                })
                .ToListAsync(ct);

            return Ok(prices);
        }

        // ------------------------------------------------------------
        // POST: /api/admin/content-products/{contentProductId}/prices
        // Create a new price plan
        // ------------------------------------------------------------
        [HttpPost("{contentProductId:int}/prices")]
        public async Task<IActionResult> CreatePrice(int contentProductId, [FromBody] UpsertContentProductPriceRequest req, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var productExists = await _db.ContentProducts
                .AsNoTracking()
                .AnyAsync(x => x.Id == contentProductId, ct);

            if (!productExists)
                return NotFound(new { message = "Content product not found." });

            var normalizedCurrency = NormalizeCurrency(req.Currency);

            // ✅ NEW: basic effective window validation
            if (req.EffectiveFromUtc.HasValue && req.EffectiveToUtc.HasValue &&
                req.EffectiveFromUtc.Value > req.EffectiveToUtc.Value)
            {
                return BadRequest(new { message = "EffectiveFromUtc must be before EffectiveToUtc." });
            }

            // ✅ NEW: prevent overlapping ACTIVE plans for same key (Audience+Billing+Currency)
            var overlap = await HasOverlappingActivePlanAsync(
                contentProductId: contentProductId,
                audience: req.Audience,
                billing: req.BillingPeriod,
                currency: normalizedCurrency,
                fromUtc: req.EffectiveFromUtc,
                toUtc: req.EffectiveToUtc,
                excludeId: null,
                ct: ct);

            if (req.IsActive && overlap)
            {
                return Conflict(new
                {
                    message = "An active pricing plan with overlapping effective dates already exists for the same Audience/Billing/Currency."
                });
            }

            var entity = new ContentProductPrice
            {
                ContentProductId = contentProductId,
                Audience = req.Audience,
                BillingPeriod = req.BillingPeriod,
                Currency = normalizedCurrency, // ✅ NEW: currency normalized to upper
                Amount = req.Amount,
                IsActive = req.IsActive,
                EffectiveFromUtc = req.EffectiveFromUtc,
                EffectiveToUtc = req.EffectiveToUtc,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.ContentProductPrices.Add(entity);
            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                entity.Id,
                entity.ContentProductId,
                entity.Audience,
                entity.BillingPeriod,
                entity.Currency,
                entity.Amount,
                entity.IsActive,
                entity.EffectiveFromUtc,
                entity.EffectiveToUtc,
                entity.CreatedAtUtc
            });
        }

        // ------------------------------------------------------------
        // PUT: /api/admin/content-products/{contentProductId}/prices/{priceId}
        // Update an existing plan
        // ------------------------------------------------------------
        [HttpPut("{contentProductId:int}/prices/{priceId:int}")]
        public async Task<IActionResult> UpdatePrice(int contentProductId, int priceId, [FromBody] UpsertContentProductPriceRequest req, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var entity = await _db.ContentProductPrices
                .FirstOrDefaultAsync(p => p.Id == priceId && p.ContentProductId == contentProductId, ct);

            if (entity == null)
                return NotFound(new { message = "Pricing plan not found." });

            var normalizedCurrency = NormalizeCurrency(req.Currency);

            // ✅ NEW: basic effective window validation
            if (req.EffectiveFromUtc.HasValue && req.EffectiveToUtc.HasValue &&
                req.EffectiveFromUtc.Value > req.EffectiveToUtc.Value)
            {
                return BadRequest(new { message = "EffectiveFromUtc must be before EffectiveToUtc." });
            }

            // ✅ NEW: prevent overlapping ACTIVE plans for same key (Audience+Billing+Currency)
            var overlap = await HasOverlappingActivePlanAsync(
                contentProductId: contentProductId,
                audience: req.Audience,
                billing: req.BillingPeriod,
                currency: normalizedCurrency,
                fromUtc: req.EffectiveFromUtc,
                toUtc: req.EffectiveToUtc,
                excludeId: entity.Id,
                ct: ct);

            if (req.IsActive && overlap)
            {
                return Conflict(new
                {
                    message = "An active pricing plan with overlapping effective dates already exists for the same Audience/Billing/Currency."
                });
            }

            entity.Audience = req.Audience;
            entity.BillingPeriod = req.BillingPeriod;
            entity.Currency = normalizedCurrency; // ✅ NEW: currency normalized to upper
            entity.Amount = req.Amount;
            entity.IsActive = req.IsActive;
            entity.EffectiveFromUtc = req.EffectiveFromUtc;
            entity.EffectiveToUtc = req.EffectiveToUtc;

            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                entity.Id,
                entity.ContentProductId,
                entity.Audience,
                entity.BillingPeriod,
                entity.Currency,
                entity.Amount,
                entity.IsActive,
                entity.EffectiveFromUtc,
                entity.EffectiveToUtc,
                entity.CreatedAtUtc
            });
        }

        // ------------------------------------------------------------
        // PATCH: /api/admin/content-products/{contentProductId}/prices/{priceId}/active
        // Toggle active without editing other fields
        // ------------------------------------------------------------
        [HttpPatch("{contentProductId:int}/prices/{priceId:int}/active")]
        public async Task<IActionResult> SetActive(int contentProductId, int priceId, [FromBody] SetPriceActiveRequest req, CancellationToken ct)
        {
            var entity = await _db.ContentProductPrices
                .FirstOrDefaultAsync(p => p.Id == priceId && p.ContentProductId == contentProductId, ct);

            if (entity == null)
                return NotFound(new { message = "Pricing plan not found." });

            if (req.IsActive)
            {
                // ✅ NEW: when turning on, enforce no overlap with other active plans of same key
                var overlap = await HasOverlappingActivePlanAsync(
                    contentProductId: contentProductId,
                    audience: entity.Audience,
                    billing: entity.BillingPeriod,
                    currency: NormalizeCurrency(entity.Currency),
                    fromUtc: entity.EffectiveFromUtc,
                    toUtc: entity.EffectiveToUtc,
                    excludeId: entity.Id,
                    ct: ct);

                if (overlap)
                {
                    return Conflict(new
                    {
                        message = "Cannot activate: another active pricing plan overlaps for the same Audience/Billing/Currency."
                    });
                }
            }

            entity.IsActive = req.IsActive;
            await _db.SaveChangesAsync(ct);

            return Ok(new { ok = true, entity.Id, entity.IsActive });
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static string NormalizeCurrency(string? currency)
        {
            var c = (currency ?? "KES").Trim().ToUpperInvariant();
            return string.IsNullOrWhiteSpace(c) ? "KES" : c;
        }

        // ✅ NEW: overlaps check for safe price lifecycle
        // ✅ NEW: overlaps check for safe price lifecycle (SQL-translatable)
        private async Task<bool> HasOverlappingActivePlanAsync(
            int contentProductId,
            PricingAudience audience,
            BillingPeriod billing,
            string currency,
            DateTime? fromUtc,
            DateTime? toUtc,
            int? excludeId,
            CancellationToken ct)
        {
            // Overlap rule (including open-ended ranges):
            // ranges overlap if:
            //   (toUtc is null OR p.From is null OR p.From <= toUtc)
            // AND
            //   (fromUtc is null OR p.To is null OR p.To >= fromUtc)

            return await _db.ContentProductPrices
                .AsNoTracking()
                .Where(p => p.ContentProductId == contentProductId)
                .Where(p => p.IsActive)
                .Where(p => p.Audience == audience && p.BillingPeriod == billing && p.Currency == currency)
                .Where(p => excludeId == null || p.Id != excludeId.Value)
                .Where(p =>
                    // ✅ overlap condition (SQL friendly)
                    (toUtc == null || p.EffectiveFromUtc == null || p.EffectiveFromUtc <= toUtc) &&
                    (fromUtc == null || p.EffectiveToUtc == null || p.EffectiveToUtc >= fromUtc)
                )
                .AnyAsync(ct);
        }

    }
}
