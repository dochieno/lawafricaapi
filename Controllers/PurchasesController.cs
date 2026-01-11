using LawAfrica.API.Models.DTOs.Purchases;
using LawAfrica.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/purchases")]
    [Authorize] // user must be logged in
    public class PurchasesController : ControllerBase
    {
        private readonly PurchaseService _purchaseService;

        public PurchasesController(PurchaseService purchaseService)
        {
            _purchaseService = purchaseService;
        }

        /// <summary>
        /// Finalizes a public purchase AFTER payment success.
        /// </summary>
        [HttpPost("complete")]
        public async Task<IActionResult> CompletePurchase(
            [FromBody] CompletePurchaseRequest request)
        {
            var userId = int.Parse(User.FindFirst("userId")!.Value);

            var ownership = await _purchaseService
                .CompletePublicPurchaseAsync(
                    userId,
                    request.ContentProductId,
                    request.TransactionReference);

            return Ok(new
            {
                message = "Purchase completed successfully.",
                ownershipId = ownership.Id
            });
        }
    }
}
