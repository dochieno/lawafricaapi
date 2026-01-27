using LawAfrica.API.Services.LawReportsContent;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/law-reports")]
    public class LawReportsAiFormatController : ControllerBase
    {
        private readonly LawReportContentBuilder _builder;

        public LawReportsAiFormatController(LawReportContentBuilder builder)
        {
            _builder = builder;
        }

        public sealed class AiFormatRequest
        {
            public bool Force { get; set; } = false;
            public int? MaxInputChars { get; set; }
        }

        [HttpPost("{lawReportId:int}/ai-format")]
        public async Task<IActionResult> AiFormat(int lawReportId, [FromBody] AiFormatRequest req, CancellationToken ct)
        {
            var (buildResult, modelUsed) = await _builder.BuildAiAsync(
                lawReportId,
                force: req?.Force ?? false,
                maxInputCharsOverride: req?.MaxInputChars,
                ct: ct);

            // If you want to return the JSON cache directly, you can load it here and return it.
            // For now, returning summary info is OK, but you asked "Output must be JSON blocks schema".
            // So: load cache and return it.

            // You likely already have DbContext inside builder; simplest is add a GetCachedJsonDto method.
            // If you prefer: expose a reader service. For brevity, add a builder method that returns the dto.

            var dto = await _builder.GetJsonDtoAsync(lawReportId, ct); // implement tiny helper (below)

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