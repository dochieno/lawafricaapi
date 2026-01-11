using LawAfrica.API.Models.DTOs.AdminDashboard;
using LawAfrica.API.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/kpis/payments")]
    [Authorize(Roles = "Admin")]
    public class AdminPaymentsKpiController : ControllerBase
    {
        private readonly AdminPaymentsKpiService _svc;

        public AdminPaymentsKpiController(AdminPaymentsKpiService svc)
        {
            _svc = svc;
        }

        [HttpPost]
        public async Task<IActionResult> Get([FromBody] PaymentsKpiRequest req, CancellationToken ct)
        {
            try
            {
                var res = await _svc.GetAsync(req, ct);
                return Ok(res);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid KPI request",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }
    }
}
