using System.Security.Claims;
using LawAfrica.API.Models.DTOs.Ai.Sections;
using LawAfrica.API.Services.Ai.Sections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers.Ai
{
    [ApiController]
    [Route("api/ai/legal-documents/sections")]
    [Authorize] // only logged in users
    public class AiLegalDocumentSectionsController : ControllerBase
    {
        private readonly ILegalDocumentSectionSummarizer _summarizer;

        public AiLegalDocumentSectionsController(ILegalDocumentSectionSummarizer summarizer)
        {
            _summarizer = summarizer;
        }

        /// <summary>
        /// Summarize a document section (by ToC entry and page range).
        /// </summary>
        [HttpPost("summarize")]
        [ProducesResponseType(typeof(SectionSummaryResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SectionSummaryResponseDto>> Summarize(
            [FromBody] SectionSummaryRequestDto request,
            CancellationToken ct)
        {
            if (request == null)
                return BadRequest(new { message = "Request body is required." });

            if (request.LegalDocumentId <= 0)
                return BadRequest(new { message = "LegalDocumentId must be a positive integer." });

            if (request.TocEntryId <= 0)
                return BadRequest(new { message = "TocEntryId must be a positive integer." });

            // We are proceeding with your assumption for now:
            // ToC uses PDF page numbers directly (no roman offset mapping yet).
            if (request.StartPage <= 0)
                return BadRequest(new { message = "StartPage must be >= 1." });

            if (request.EndPage <= 0)
                return BadRequest(new { message = "EndPage must be >= 1." });

            if (request.EndPage < request.StartPage)
                return BadRequest(new { message = "EndPage must be >= StartPage." });

            // userId from JWT claims
            var userIdStr =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub");

            if (!int.TryParse(userIdStr, out var userId) || userId <= 0)
                return Unauthorized(new { message = "Invalid user identity." });

            var result = await _summarizer.SummarizeAsync(request, userId, ct);
            return Ok(result);
        }
    }
}
