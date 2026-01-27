using System.Threading;
using System.Threading.Tasks;
using LawAfrica.API.Services.LawReportsContent;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/law-reports")]
    public class LawReportsAiFormatController : ControllerBase
    {
        private readonly ILawReportContentBuilder _builder;

        public LawReportsAiFormatController(ILawReportContentBuilder builder)
        {
            _builder = builder;
        }

        public sealed class AiFormatRequest
        {
            public bool Force { get; set; } = false;
            public int? MaxInputChars { get; set; }
        }

        [HttpPost("{lawReportId:int}/ai-format")]
        public async Task<IActionResult> AiFormat(
            int lawReportId,
            [FromBody] AiFormatRequest? req,
            CancellationToken ct)
        {
            var (buildResult, modelUsed) = await _builder.BuildAiAsync(
                lawReportId,
                force: req?.Force ?? false,
                maxInputCharsOverride: req?.MaxInputChars,
                ct: ct);

            var dto = await _builder.GetJsonDtoAsync(lawReportId, ct);

            return Ok(new
            {
                dto.LawReportId,
                dto.Hash,
                built = buildResult.Built,
                modelUsed,
                blocksWritten = buildResult.BlocksWritten,
                blocks = dto.Blocks
            });
        }
    }
}