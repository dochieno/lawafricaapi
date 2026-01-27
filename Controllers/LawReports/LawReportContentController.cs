using System.Threading;
using System.Threading.Tasks;
using LawAfrica.API.Services.LawReportsContent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers.LawReports
{
    [ApiController]
    [Route("api/law-reports/{lawReportId:int}/content")]
    [Authorize] // keep authenticated; you can tighten to admin later
    public class LawReportContentController : ControllerBase
    {
        private readonly ILawReportContentBuilder _builder;

        public LawReportContentController(ILawReportContentBuilder builder)
        {
            _builder = builder;
        }

        [HttpPost("build")]
        public async Task<IActionResult> Build(int lawReportId, [FromQuery] bool force = false, CancellationToken ct = default)
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
    }
}