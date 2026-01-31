// Controllers/Ai/AiLegalDocumentsController.cs
using System.Security.Claims;
using LawAfrica.API.Models.DTOs.Ai.Sections;
using LawAfrica.API.Services.Ai.Sections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers.Ai
{
    [ApiController]
    [Route("api/ai/legal-documents")]
    [Authorize] // only logged in users can use AI
    public class AiLegalDocumentsController : ControllerBase
    {
        private readonly ILegalDocumentSectionSummarizer _sectionSummarizer;

        public AiLegalDocumentsController(ILegalDocumentSectionSummarizer sectionSummarizer)
        {
            _sectionSummarizer = sectionSummarizer;
        }

        [HttpPost("sections/summarize")]
        [ProducesResponseType(typeof(SectionSummaryResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(object), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SummarizeSection(
            [FromBody] SectionSummaryRequestDto request,
            CancellationToken ct)
        {
            if (request == null)
                return BadRequest(new { message = "Request body is required." });

            if (request.LegalDocumentId <= 0)
                return BadRequest(new { message = "LegalDocumentId must be a positive integer." });

            // TocEntryId is optional (nullable) in your DTO. If provided, it must be > 0.
            if (request.TocEntryId.HasValue && request.TocEntryId.Value <= 0)
                return BadRequest(new { message = "TocEntryId must be a positive integer when provided." });

            if (request.StartPage <= 0)
                return BadRequest(new { message = "StartPage must be >= 1." });

            if (request.EndPage <= 0)
                return BadRequest(new { message = "EndPage must be >= 1." });

            if (request.EndPage < request.StartPage)
                return BadRequest(new { message = "EndPage must be >= StartPage." });

            var userId = GetUserIdIntOrThrow();

            try
            {
                var result = await _sectionSummarizer.SummarizeAsync(request, userId, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                // quota exceeded / business guardrails
                // return 429 to mirror "limit reached" semantics used elsewhere
                return StatusCode(429, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to generate section summary.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        private int GetUserIdIntOrThrow()
        {
            // Works with Identity/JWT; fallback to "sub" / "userId" if needed
            var raw =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.FindFirstValue("userId") ??
                "";

            if (!int.TryParse(raw, out var userId) || userId <= 0)
                throw new InvalidOperationException("Invalid user id in token.");

            return userId;
        }
    }
}
