using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/my-library")]
    [Authorize]
    public class MyLibraryController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public MyLibraryController(ApplicationDbContext db)
        {
            _db = db;
        }

        // --------------------------------------------
        // GET: My Library
        // --------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetMyLibrary()
        {
            var userId = User.GetUserId();

            var items = await _db.UserLibraries
                .Where(x => x.UserId == userId)
                .Include(x => x.LegalDocument)
                .ThenInclude(d => d.Country)
                .Select(x => new
                {
                    x.LegalDocument.Id,
                    x.LegalDocument.Title,
                    x.LegalDocument.Author,
                    x.LegalDocument.Category,
                    x.LegalDocument.IsPremium,
                    x.LegalDocument.CoverImagePath,
                    x.AccessType,
                    x.AddedAt
                })
                .ToListAsync();

            return Ok(items);
        }

        // --------------------------------------------
        // POST: Add document to library
        // --------------------------------------------




            // POST: /api/my-library/{documentId}
            [HttpPost("{documentId:int}")]
            public async Task<IActionResult> AddToLibrary(
                int documentId,
                [FromServices] DocumentEntitlementService entitlementService)
            {
                var userId = User.GetUserId();

                var doc = await _db.LegalDocuments
                    .FirstOrDefaultAsync(d => d.Id == documentId && d.Status == LegalDocumentStatus.Published);

                if (doc == null)
                    return NotFound("Document not found or unpublished.");

                var exists = await _db.UserLibraries.AnyAsync(x =>
                    x.UserId == userId && x.LegalDocumentId == documentId);

                if (exists)
                    return BadRequest("Document already in your library.");

                // ✅ Free doc => always allow
                if (!doc.IsPremium)
                {
                    _db.UserLibraries.Add(new UserLibrary
                    {
                        UserId = userId,
                        LegalDocumentId = documentId,
                        AccessType = LibraryAccessType.Free
                    });

                    await _db.SaveChangesAsync();
                    return Ok(new { message = "Added to library." });
                }

                // ✅ Premium doc => allow only if entitled (bundle/purchase/subscription/etc)
                var accessLevel = await entitlementService.GetAccessLevelAsync(userId, doc);
                if (accessLevel != DocumentAccessLevel.FullAccess)
                    return Forbid("Access required.");

                // ✅ Add as bookmark (do NOT grant Subscription/Purchase/AdminGrant)
                _db.UserLibraries.Add(new UserLibrary
                {
                    UserId = userId,
                    LegalDocumentId = documentId,
                    AccessType = LibraryAccessType.Free
                });

                await _db.SaveChangesAsync();
                return Ok(new { message = "Added to library." });
            }

            // (keep your other endpoints unchanged: GET /api/my-library, DELETE, etc)
        
    



    // --------------------------------------------
    // DELETE: Remove from library (optional)
    // --------------------------------------------
       [HttpDelete("{documentId}")]
        public async Task<IActionResult> RemoveFromLibrary(int documentId)
        {
            var userId = User.GetUserId();

            var item = await _db.UserLibraries.FirstOrDefaultAsync(x =>
                x.UserId == userId &&
                x.LegalDocumentId == documentId);

            if (item == null)
                return NotFound();

            _db.UserLibraries.Remove(item);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Removed from library." });
        }
    }
}
