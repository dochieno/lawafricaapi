using LawAfrica.API.Authorization.Policies;
using LawAfrica.API.Models.DTOs.Institutions;
using LawAfrica.API.Services.Institutions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class InstitutionsController : ControllerBase
    {
        private readonly InstitutionService _service;

        public InstitutionsController(InstitutionService service)
        {
            _service = service;
        }

        [AllowAnonymous]
        [HttpGet("public")]
        public async Task<IActionResult> GetPublic([FromQuery] string? q = null)
        {
            var items = await _service.GetPublicAsync(q);
            return Ok(items);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? q = null)
        {
            var items = await _service.GetAllAsync(q);
            return Ok(items);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound("Institution not found.");
            return Ok(item);
        }

        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateInstitutionRequest request)
        {
            var id = await _service.CreateAsync(request);
            return Ok(new { id, message = "Institution created." });
        }

        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateInstitutionRequest request)
        {
            await _service.UpdateAsync(id, request);
            return Ok(new { message = "Institution updated." });
        }

        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpPost("{id:int}/activate")]
        public async Task<IActionResult> Activate(int id)
        {
            await _service.SetActiveAsync(id, true);
            return Ok(new { message = "Institution activated." });
        }

        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpPost("{id:int}/deactivate")]
        public async Task<IActionResult> Deactivate(int id)
        {
            await _service.SetActiveAsync(id, false);
            return Ok(new { message = "Institution deactivated." });
        }

        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpPost("{id:int}/verify")]
        public async Task<IActionResult> Verify(int id)
        {
            await _service.SetVerifiedAsync(id, true);
            return Ok(new { message = "Institution verified." });
        }

        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpPost("{id:int}/unverify")]
        public async Task<IActionResult> Unverify(int id)
        {
            await _service.SetVerifiedAsync(id, false);
            return Ok(new { message = "Institution unverified." });
        }

       // Allow admin to have access [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpGet("{institutionId:int}/users")]
        public async Task<IActionResult> GetUsers(int institutionId)
        {
            var users = await _service.GetUsersAsync(institutionId);
            return Ok(users);
        }

        // ✅ NEW: Global Admin decides if institution users can buy individually when subscription is inactive
        public class UpdateInstitutionPurchasePolicyRequest
        {
            public bool AllowIndividualPurchasesWhenInstitutionInactive { get; set; }
        }

        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpPost("{id:int}/purchase-policy")]
        public async Task<IActionResult> UpdatePurchasePolicy(int id, [FromBody] UpdateInstitutionPurchasePolicyRequest request)
        {
            await _service.SetAllowIndividualPurchasesWhenInstitutionInactiveAsync(
                id,
                request.AllowIndividualPurchasesWhenInstitutionInactive
            );

            return Ok(new
            {
                message = "Purchase policy updated.",
                institutionId = id,
                allowIndividualPurchasesWhenInstitutionInactive = request.AllowIndividualPurchasesWhenInstitutionInactive
            });
        }
    }
}
