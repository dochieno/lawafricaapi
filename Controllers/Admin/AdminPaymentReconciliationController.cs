using LawAfrica.API.Helpers;
using LawAfrica.API.Models.DTOs.Payments.Reconciliation;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/reconciliation")]
    [Authorize(Roles = "Admin")]
    public class AdminPaymentReconciliationController : ControllerBase
    {
        private readonly PaymentReconciliationService _svc;

        public AdminPaymentReconciliationController(PaymentReconciliationService svc)
        {
            _svc = svc;
        }

        // POST api/admin/reconciliation/run
        [HttpPost("run")]
        public async Task<IActionResult> Run([FromBody] RunReconciliationRequest request, CancellationToken ct)
        {
            try
            {
                var adminUserId = User.GetUserId();
                var result = await _svc.RunAsync(request, adminUserId, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Reconciliation request invalid",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        // POST api/admin/reconciliation/manual
        [HttpPost("manual")]
        public async Task<IActionResult> Manual([FromBody] ManualReconcileRequest request, CancellationToken ct)
        {
            try
            {
                var adminUserId = User.GetUserId();
                var runId = await _svc.ManualReconcileAsync(request, adminUserId, ct);
                return Ok(new { message = "Manual reconcile applied.", runId });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Manual reconcile failed",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        // GET api/admin/reconciliation/report?provider=Paystack&fromUtc=...&toUtc=...&status=Mismatch&reason=AmountMismatch&skip=0&take=50
        [HttpGet("report")]
        public async Task<IActionResult> Report(
            [FromQuery] PaymentProvider? provider,
            [FromQuery] DateTime fromUtc,
            [FromQuery] DateTime toUtc,
            [FromQuery] ReconciliationStatus? status,
            [FromQuery] ReconciliationReason? reason,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 50,
            CancellationToken ct = default)
        {
            try
            {
                var report = await _svc.GetReportAsync(provider, fromUtc, toUtc, status, reason, skip, take, ct);
                return Ok(report);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Report request invalid",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }
    }
}
