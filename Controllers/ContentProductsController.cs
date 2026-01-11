using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/content-products")]
    [Authorize(Roles = "Admin")]
    public class ContentProductsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public ContentProductsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // --------------------------------------------------
        // GET: list all content products
        // --------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var products = await _db.ContentProducts
                .AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
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
                .ToListAsync();

            return Ok(products);
        }

        // --------------------------------------------------
        // GET: single content product
        // --------------------------------------------------
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var product = await _db.ContentProducts
                .AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new ContentProductDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,

                    AccessModel = p.PublicAccessModel,

                    InstitutionAccessModel = p.InstitutionAccessModel,
                    PublicAccessModel = p.PublicAccessModel,
                    IncludedInInstitutionBundle = p.IncludedInInstitutionBundle,
                    IncludedInPublicBundle = p.IncludedInPublicBundle,

                    AvailableToInstitutions = p.AvailableToInstitutions,
                    AvailableToPublic = p.AvailableToPublic,
                    CreatedAt = p.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (product == null)
                return NotFound("Content product not found.");

            return Ok(product);
        }

        // --------------------------------------------------
        // POST: create content product
        // --------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateContentProductRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required.");

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Name is required.");

            // Legacy compatibility: AccessModel overrides PublicAccessModel if provided
            var publicAccess = request.PublicAccessModel;
            if (request.AccessModel != ProductAccessModel.Unknown)
                publicAccess = request.AccessModel;

            var product = new ContentProduct
            {
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),

                InstitutionAccessModel = request.InstitutionAccessModel,
                PublicAccessModel = publicAccess,

                IncludedInInstitutionBundle = request.IncludedInInstitutionBundle,
                IncludedInPublicBundle = request.IncludedInPublicBundle,

                AvailableToInstitutions = request.AvailableToInstitutions,
                AvailableToPublic = request.AvailableToPublic,

                // Legacy mirror
                AccessModel = publicAccess,

                CreatedAt = DateTime.UtcNow
            };

            _db.ContentProducts.Add(product);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Content product created successfully.",
                productId = product.Id
            });
        }

        // --------------------------------------------------
        // PUT: update content product
        // --------------------------------------------------
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(
            int id,
            [FromBody] UpdateContentProductRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required.");

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Name is required.");

            var product = await _db.ContentProducts.FirstOrDefaultAsync(p => p.Id == id);
            if (product == null)
                return NotFound("Content product not found.");

            var publicAccess = request.PublicAccessModel;
            if (request.AccessModel != ProductAccessModel.Unknown)
                publicAccess = request.AccessModel;

            product.Name = request.Name.Trim();
            product.Description = request.Description?.Trim();

            product.InstitutionAccessModel = request.InstitutionAccessModel;
            product.PublicAccessModel = publicAccess;

            product.IncludedInInstitutionBundle = request.IncludedInInstitutionBundle;
            product.IncludedInPublicBundle = request.IncludedInPublicBundle;

            product.AvailableToInstitutions = request.AvailableToInstitutions;
            product.AvailableToPublic = request.AvailableToPublic;

            product.AccessModel = publicAccess; // legacy mirror

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Content product updated successfully.",
                productId = product.Id
            });
        }
    }
}
