using LawAfrica.API.Data;
using LawAfrica.API.Helpers; // User.GetUserId()
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Models.LawReportsContent;
using LawAfrica.API.Models.LawReportsContent.Models;
using LawAfrica.API.Services.Documents;
using LawAfrica.API.Services.LawReports;
using LawAfrica.API.Services.LawReportsContent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;

namespace LawAfrica.API.Controllers.LawReports
{
    [ApiController]
    [Route("api/law-reports/{lawReportId:int}/content")]
    [Authorize]
    public class LawReportContentController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILawReportContentBuilder _builder;
        private readonly DocumentEntitlementService _entitlement;

        public LawReportContentController(
            ApplicationDbContext db,
            ILawReportContentBuilder builder,
            DocumentEntitlementService entitlement)
        {
            _db = db;
            _builder = builder;
            _entitlement = entitlement;
        }

        private async Task<(bool ok, DocumentEntitlementDecision? decision, int? legalDocumentId, IActionResult? fail)>
            EnsureEntitledAsync(int lawReportId, CancellationToken ct)
        {
            var userId = User.GetUserId();

            // 1) Find linked LegalDocumentId
            var ld = await _db.LawReports
                .AsNoTracking()
                .Where(r => r.Id == lawReportId)
                .Select(r => r.LegalDocumentId)
                .FirstOrDefaultAsync(ct);

            if (ld == null || ld <= 0)
            {
                // If you ever have reports without a legal document, treat them as locked to be safe
                return (false, null, null, NotFound(new { message = "Linked legalDocumentId not found for this report." }));
            }

            // 2) Load legal document
            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == ld, ct);

            if (doc == null)
                return (false, null, ld, NotFound(new { message = "Linked legal document not found." }));

            // 3) Non-premium => always ok
            if (!doc.IsPremium)
                return (true, null, ld, null);

            // 4) Evaluate entitlement
            var decision = await _entitlement.GetEntitlementDecisionAsync(userId, doc);

            // Hard-block (institution inactive / seat exceeded) => forbid completely
            var hardBlocked =
                !decision.IsAllowed &&
                (decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSubscriptionInactive ||
                 decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSeatLimitExceeded);

            if (hardBlocked)
            {
                return (false, decision, ld, StatusCode(403, new
                {
                    message = decision.Message ?? "Access blocked. Please contact your administrator.",
                    denyReason = decision.DenyReason.ToString(),
                    hardStop = true
                }));
            }

            // Otherwise ok (FullAccess OR PreviewOnly) — caller decides how to respond
            return (true, decision, ld, null);
        }

        // POST /api/law-reports/{id}/content/build?force=true
        // ✅ Recommend: only Admin builds normalized cache
        [Authorize(Roles = "Admin")]
        [HttpPost("build")]
        public async Task<IActionResult> Build(
            [FromRoute] int lawReportId,
            [FromQuery] bool force = false,
            CancellationToken ct = default)
        {
            var r = await _builder.BuildAsync(lawReportId, force, ct);

            return Ok(new
            {
                lawReportId = r.LawReportId,
                built = r.Built,
                hash = r.Hash,
                blocksWritten = r.BlocksWritten
            });
        }

        // GET /api/law-reports/{id}/content/json?forceBuild=true
        [HttpGet("json")]
        public async Task<IActionResult> GetJson(
            [FromRoute] int lawReportId,
            [FromQuery] bool forceBuild = false,
            CancellationToken ct = default)
        {
            // ✅ Gate access
            var gate = await EnsureEntitledAsync(lawReportId, ct);
            if (!gate.ok)
                return gate.fail!;

            // If premium + preview-only => return preview JSON (not full normalized blocks)
            if (gate.decision != null && gate.decision.AccessLevel != DocumentAccessLevel.FullAccess)
            {
                // Pull raw contentText from LawReports table (or wherever your transcript lives)
                var raw = await _db.LawReports
                    .AsNoTracking()
                    .Where(r => r.Id == lawReportId)
                    .Select(r => r.ContentText)
                    .FirstOrDefaultAsync(ct) ?? "";

                var maxChars = gate.decision.PreviewMaxChars ?? 2200;
                var maxParas = gate.decision.PreviewMaxParagraphs ?? 6;

                var preview = ReportPreviewTruncator.MakePreview(raw, maxChars, maxParas);

                // ✅ Return a preview-shaped JSON the client can still render
                // (You can adjust this shape to match your reader if needed)
                return Ok(new
                {
                    lawReportId,
                    legalDocumentId = gate.legalDocumentId,
                    access = "Preview",
                    previewMaxChars = maxChars,
                    previewMaxParagraphs = maxParas,
                    hardStop = gate.decision.HardStop,
                    message = gate.decision.Message ?? "Preview mode",
                    // simple payload
                    previewText = preview
                });
            }

            // Full access => return normalized cache
            var cache = await _db.Set<LawReportContentJsonCache>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.LawReportId == lawReportId, ct);

            if (cache == null || forceBuild)
            {
                // Building normalized cache can be heavy — keep as admin-only if you prefer
                await _builder.BuildAsync(lawReportId, force: forceBuild, ct: ct);

                cache = await _db.Set<LawReportContentJsonCache>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.LawReportId == lawReportId, ct);
            }

            if (cache == null)
                return NotFound(new { message = "No normalized content cache found for this report." });

            return Content(cache.Json ?? "{}", "application/json");
        }

        // GET /api/law-reports/{id}/content/json/status
        [Authorize(Roles = "Admin")]
        [HttpGet("json/status")]
        public async Task<IActionResult> GetJsonStatus(
            [FromRoute] int lawReportId,
            CancellationToken ct = default)
        {
            var cache = await _db.Set<LawReportContentJsonCache>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.LawReportId == lawReportId, ct);

            if (cache == null)
                return Ok(new { lawReportId, exists = false });

            var blocksCount = await _db.Set<LawReportContentBlock>()
                .AsNoTracking()
                .CountAsync(x => x.LawReportId == lawReportId, ct);

            return Ok(new
            {
                lawReportId,
                exists = true,
                hash = cache.Hash,
                builtBy = cache.BuiltBy,
                createdAt = cache.BuiltAt,
                updatedAt = cache.UpdatedAt,
                blocksCount
            });
        }


    }
}
