using System.Security.Claims;
using LawAfrica.API.Models.DTOs.Ai.Sections;
using LawAfrica.API.Services.Ai.Sections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers.Ai
{
    [ApiController]
    [Route("api/ai/legal-documents/sections")]
    [Authorize] // only logged in users can use AI
    public class LegalDocumentSectionsAiController : ControllerBase
    {
        private readonly ILegalDocumentSectionSummarizer _summarizer;

        public LegalDocumentSectionsAiController(ILegalDocumentSectionSummarizer summarizer)
        {
            _summarizer = summarizer;
        }

        [HttpPost("summarize")]
        public async Task<ActionResult<SectionSummaryResponseDto>> Summarize(
            [FromBody] SectionSummaryRequestDto request,
            CancellationToken ct)
        {
            if (request == null)
                return BadRequest(new { message = "Request body is required." });

            // Basic validation (service can also validate deeper)
            if (request.StartPage <= 0 || request.EndPage <= 0)
                return BadRequest(new { message = "StartPage and EndPage must be >= 1." });

            if (request.EndPage < request.StartPage)
                return BadRequest(new { message = "EndPage must be >= StartPage." });

            var userId = GetUserIdOrThrow();

            try
            {
                var result = await _summarizer.SummarizeAsync(request, userId, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                // quota exceeded etc.
                return BadRequest(new { message = ex.Message });
            }
        }

        private int GetUserIdOrThrow()
        {
            var raw =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.FindFirstValue("userId");

            if (!int.TryParse(raw, out var userId) || userId <= 0)
                throw new InvalidOperationException("Invalid user id in token.");

            return userId;
        }
    }
}
