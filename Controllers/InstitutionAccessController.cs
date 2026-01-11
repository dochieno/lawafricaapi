using LawAfrica.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/institutions")]
    [Authorize(Roles = "Admin")]
    public class InstitutionAccessController : ControllerBase
    {
        private readonly InstitutionAccessService _access;

        public InstitutionAccessController(InstitutionAccessService access)
        {
            _access = access;
        }

        /// <summary>
        /// Centralized access check for institution -> product.
        /// Applies bundle vs separate rules + excluded-from-bundle products.
        /// </summary>
        [HttpGet("{institutionId:int}/products/{productId:int}/access")]
        public async Task<IActionResult> CheckAccess([FromRoute] int institutionId, [FromRoute] int productId)
        {
            var result = await _access.CheckInstitutionProductAccessAsync(institutionId, productId, DateTime.UtcNow);
            return Ok(result);
        }
    }
}
