using LawAfrica.API.Data;
using LawAfrica.API.Models.LawReportsContent;
using LawAfrica.API.Models.LawReportsContent.Models;
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

        public LawReportContentController(ApplicationDbContext db, ILawReportContentBuilder builder)
        {
            _db = db;
            _builder = builder;
        }

        // POST /api/law-reports/{id}/content/build?force=true
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
            var cache = await _db.Set<LawReportContentJsonCache>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.LawReportId == lawReportId, ct);

            if (cache == null || forceBuild)
            {
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
                createdAt = cache.BuiltAt,   // ⚠️ if your property is BuiltAt, change to BuiltAt
                updatedAt = cache.UpdatedAt,
                blocksCount
            });
        }
    }
}