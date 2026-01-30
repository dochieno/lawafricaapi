using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.LegalDocuments
{
    [ApiController]
    [Route("api/legal-documents/{id:int}/toc")]
    [Authorize] // ✅ reader endpoint requires login
    public class LegalDocumentTocController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly LegalDocumentTocService _toc;
        private readonly DocumentEntitlementService _entitlement;

        public LegalDocumentTocController(
            ApplicationDbContext db,
            LegalDocumentTocService toc,
            DocumentEntitlementService entitlement)
        {
            _db = db;
            _toc = toc;
            _entitlement = entitlement;
        }

        // ✅ Reader ToC (no Notes)
        [HttpGet]
        public async Task<IActionResult> Get(int id)
        {
            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id && d.Status == LegalDocumentStatus.Published);

            if (doc == null)
                return NotFound(new { message = "Document not found." });

            var userId = User.GetUserId();
            var decision = await _entitlement.GetEntitlementDecisionAsync(userId, doc);

            // ✅ Hard blocks
            if (!decision.IsAllowed &&
                (decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSubscriptionInactive ||
                 decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSeatLimitExceeded))
            {
                return StatusCode(403, new
                {
                    message = decision.Message ?? "Access blocked.",
                    denyReason = decision.DenyReason.ToString(),
                    source = "READER_TOC_V3_BLOCK"
                });
            }

            var tree = await _toc.GetTreeAsync(id, includeAdminFields: false);

            return Ok(new
            {
                items = tree,
                count = tree.Count,
                source = "READER_TOC_V3_DB"
            });
        }
    }
}
