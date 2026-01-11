using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/legal-documents")]
    public class LegalDocumentPurchasesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public LegalDocumentPurchasesController(ApplicationDbContext db)
        {
            _db = db;
        }

        // --------------------------------------------------
        // PURCHASE (Public individual)
        // POST: /api/legal-documents/{id}/purchase
        // --------------------------------------------------
        [Authorize]
        [HttpPost("{id:int}/public-purchase")]
        public async Task<IActionResult> Purchase(int id)
        {
            var userId = User.GetUserId();

            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return Unauthorized();

            // Only Public individual can buy documents
            var isPublicIndividual = user.UserType == UserType.Public && user.InstitutionId == null;
            if (!isPublicIndividual)
                return Forbid("Only public individual accounts can purchase documents.");

            var doc = await _db.LegalDocuments
                .FirstOrDefaultAsync(d => d.Id == id && d.Status == LegalDocumentStatus.Published);

            if (doc == null)
                return NotFound("Document not found.");

            if (!doc.AllowPublicPurchase || doc.PublicPrice == null || doc.PublicPrice <= 0)
                return BadRequest("This document is not available for individual purchase.");

            // Already owned?
            var exists = await _db.UserLegalDocumentPurchases
                .AnyAsync(p => p.UserId == userId && p.LegalDocumentId == id);

            if (exists)
                return Ok(new { message = "Already purchased.", legalDocumentId = id });

            // NOTE: For now this is a "record purchase" endpoint.
            // Later you can require successful payment confirmation.
            var purchase = new UserLegalDocumentPurchase
            {
                UserId = userId,
                LegalDocumentId = id,
                Amount = doc.PublicPrice.Value,
                Currency = doc.PublicCurrency ?? "KES",
                PurchasedAt = DateTime.UtcNow
            };

            _db.UserLegalDocumentPurchases.Add(purchase);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // In case of race condition (unique index)
                return Ok(new { message = "Already purchased.", legalDocumentId = id });
            }

            return Ok(new
            {
                message = "Purchase recorded.",
                legalDocumentId = id,
                amount = purchase.Amount,
                currency = purchase.Currency,
                purchasedAt = purchase.PurchasedAt
            });
        }
    }
}
