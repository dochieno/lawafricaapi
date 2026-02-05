using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/pricing")]
    public class PricingController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public PricingController(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Public pricing: returns active plans for products available to public.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("public")]
        public async Task<IActionResult> GetPublicPricing(CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            var items = await _db.ContentProducts
                .AsNoTracking()
                .Where(p => p.AvailableToPublic)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Description,
                    p.PublicAccessModel,
                    p.IncludedInPublicBundle,
                    plans = _db.ContentProductPrices
                        .AsNoTracking()
                        .Where(x =>
                            x.ContentProductId == p.Id &&
                            x.Audience == PricingAudience.Public &&
                            x.IsActive &&
                            (x.EffectiveFromUtc == null || x.EffectiveFromUtc <= now) &&
                            (x.EffectiveToUtc == null || x.EffectiveToUtc >= now))
                        .OrderBy(x => x.Currency)
                        .ThenBy(x => x.BillingPeriod)
                        .Select(x => new
                        {
                            x.Id,
                            x.BillingPeriod,
                            x.Currency,
                            x.Amount,
                            x.EffectiveFromUtc,
                            x.EffectiveToUtc
                        })
                        .ToList()
                })
                .ToListAsync(ct);

            return Ok(new { items });
        }

        /// <summary>
        /// Institution pricing: returns active plans for products available to institutions.
        /// NOTE: if you want to restrict to InstitutionAdmins only, change AllowAnonymous to Authorize.
        /// </summary>
        [Authorize]
        [HttpGet("institution")]
        public async Task<IActionResult> GetInstitutionPricing(CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            // Optional: restrict to institution users only (uncomment if you want)
            // var claims = getAuthClaims? (you use helpers elsewhere)
            // if (!User.IsInstitutionUser()) return Forbid();

            var items = await _db.ContentProducts
                .AsNoTracking()
                .Where(p => p.AvailableToInstitutions)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Description,
                    p.InstitutionAccessModel,
                    p.IncludedInInstitutionBundle,
                    plans = _db.ContentProductPrices
                        .AsNoTracking()
                        .Where(x =>
                            x.ContentProductId == p.Id &&
                            x.Audience == PricingAudience.Institution &&
                            x.IsActive &&
                            (x.EffectiveFromUtc == null || x.EffectiveFromUtc <= now) &&
                            (x.EffectiveToUtc == null || x.EffectiveToUtc >= now))
                        .OrderBy(x => x.Currency)
                        .ThenBy(x => x.BillingPeriod)
                        .Select(x => new
                        {
                            x.Id,
                            x.BillingPeriod,
                            x.Currency,
                            x.Amount,
                            x.EffectiveFromUtc,
                            x.EffectiveToUtc
                        })
                        .ToList()
                })
                .ToListAsync(ct);

            return Ok(new { items });
        }

        /// <summary>
        /// Single plan lookup (server-side validation for checkout)
        /// </summary>
        [AllowAnonymous]
        [HttpGet("plan/{planId:int}")]
        public async Task<IActionResult> GetPlan(int planId, CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            var plan = await _db.ContentProductPrices
                .AsNoTracking()
                .Where(x => x.Id == planId)
                .Select(x => new
                {
                    x.Id,
                    x.ContentProductId,
                    x.Audience,
                    x.BillingPeriod,
                    x.Currency,
                    x.Amount,
                    x.IsActive,
                    x.EffectiveFromUtc,
                    x.EffectiveToUtc,
                    product = new
                    {
                        x.ContentProduct.Id,
                        x.ContentProduct.Name,
                        x.ContentProduct.AvailableToPublic,
                        x.ContentProduct.AvailableToInstitutions,
                        x.ContentProduct.PublicAccessModel,
                        x.ContentProduct.InstitutionAccessModel
                    }
                })
                .FirstOrDefaultAsync(ct);

            if (plan == null)
                return NotFound("Pricing plan not found.");

            // Soft validity check (useful for UI)
            var selectable =
                plan.IsActive &&
                (!plan.EffectiveFromUtc.HasValue || plan.EffectiveFromUtc.Value <= now) &&
                (!plan.EffectiveToUtc.HasValue || plan.EffectiveToUtc.Value >= now);

            return Ok(new { plan, selectable });
        }
    }
}
