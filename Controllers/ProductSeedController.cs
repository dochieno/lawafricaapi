using LawAfrica.API.Constants;
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/admin/seed")]
    [Authorize(Roles = "Admin")]
    public class ProductSeedController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public ProductSeedController(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Creates the institution bundle product if it doesn't already exist.
        /// Use this ONCE per environment.
        /// </summary>
        [HttpPost("institution-bundle-product")]
        public async Task<IActionResult> EnsureInstitutionBundleProduct()
        {
            var existing = await _db.ContentProducts
                .FirstOrDefaultAsync(p => p.Name == ProductIds.InstitutionBundleProductName);

            if (existing != null)
            {
                return Ok(new
                {
                    message = "Bundle product already exists.",
                    productId = existing.Id
                });
            }

            var bundle = new ContentProduct
            {
                Name = ProductIds.InstitutionBundleProductName,
                Description =
                    "Institution-wide subscription covering all bundle-included products. " +
                    "Use Institution Subscriptions to activate/extend.",

                // Legacy mirror (keep stable)
                AccessModel = ProductAccessModel.Subscription,

                // Institution access is subscription
                InstitutionAccessModel = ProductAccessModel.Subscription,

                // Public is not allowed
                PublicAccessModel = ProductAccessModel.Unknown,
                AvailableToPublic = false,

                // Bundle product itself is NOT included in bundle (it is the bundle)
                IncludedInInstitutionBundle = false,
                IncludedInPublicBundle = false,

                AvailableToInstitutions = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.ContentProducts.Add(bundle);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Bundle product created.",
                productId = bundle.Id
            });
        }
    }
}
