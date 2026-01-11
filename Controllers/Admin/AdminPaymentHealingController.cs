using LawAfrica.API.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/payments/heal")]
    [Authorize(Roles = "Admin")]
    public class AdminPaymentHealingController : ControllerBase
    {
        private readonly PaymentHealingService _svc;

        public AdminPaymentHealingController(PaymentHealingService svc)
        {
            _svc = svc;
        }

        [HttpPost("run")]
        public async Task<IActionResult> Run(CancellationToken ct)
        {
            var result = await _svc.RunAsync(ct);
            return Ok(result);
        }
    }
}
