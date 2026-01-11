using LawAfrica.API.Authorization;
using LawAfrica.API.Authorization.Policies;
using LawAfrica.API.Helpers;
using LawAfrica.API.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/payments/history")]
    [Authorize]
    public class PaymentHistoryController : ControllerBase
    {
        private readonly PaymentQueryService _queryService;

        public PaymentHistoryController(PaymentQueryService queryService)
        {
            _queryService = queryService;
        }

        /// <summary>
        /// Public user: view own payment history
        /// </summary>
        [HttpGet("me")]
        public async Task<IActionResult> MyPayments()
        {
            var userId = User.GetUserId();
            var payments = await _queryService.GetUserPaymentsAsync(userId);
            return Ok(payments);
        }

        /// <summary>
        /// Institution admin: view institution payments
        /// </summary>
        [HttpGet("institution/{institutionId}")]
        [Authorize(Policy = PolicyNames.IsInstitutionAdmin)]
        public async Task<IActionResult> InstitutionPayments(int institutionId)
        {
            var payments = await _queryService.GetInstitutionPaymentsAsync(institutionId);
            return Ok(payments);
        }

        /// <summary>
        /// Global admin: view payment details
        /// </summary>
        [HttpGet("{paymentIntentId}")]
        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        public async Task<IActionResult> PaymentDetail(int paymentIntentId)
        {
            var payment = await _queryService.GetPaymentDetailAsync(paymentIntentId);
            if (payment == null)
                return NotFound();

            return Ok(payment);
        }
    }
}
