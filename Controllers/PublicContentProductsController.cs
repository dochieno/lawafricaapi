// ✅ src/Controllers/PublicContentProductsController.cs
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/public/content-products")]
    public class PublicContentProductsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public PublicContentProductsController(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Public list of content products that can be subscribed to by public users.
        /// Condition:
        /// AvailableToPublic == true
        /// PublicAccessModel == Subscription (2)
        /// </summary>
        [AllowAnonymous]
        [HttpGet("subscription-products")]
        public async Task<ActionResult<List<ContentProductDto>>> GetPublicSubscriptionProducts(CancellationToken ct)
        {
            var products = await _db.ContentProducts
                .AsNoTracking()
                .Where(p =>
                    p.AvailableToPublic &&
                    p.PublicAccessModel == ProductAccessModel.Subscription)
                .OrderBy(p => p.Name)
                .Select(p => new ContentProductDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,

                    // Legacy mirror (older UI compatibility)
                    AccessModel = p.PublicAccessModel,

                    InstitutionAccessModel = p.InstitutionAccessModel,
                    PublicAccessModel = p.PublicAccessModel,
                    IncludedInInstitutionBundle = p.IncludedInInstitutionBundle,
                    IncludedInPublicBundle = p.IncludedInPublicBundle,

                    AvailableToInstitutions = p.AvailableToInstitutions,
                    AvailableToPublic = p.AvailableToPublic,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync(ct);

            return Ok(products);
        }
    }
}
