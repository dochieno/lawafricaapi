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

            // TocEntryId optional but if provided must be > 0
            if (request.TocEntryId.HasValue && request.TocEntryId.Value <= 0)
                return BadRequest(new { message = "TocEntryId must be a positive integer when provided." });

            var type = (request.Type ?? "basic").Trim().ToLowerInvariant();
            if (type != "basic" && type != "extended")
                return BadRequest(new { message = "Type must be 'basic' or 'extended'." });

            // ✅ Validation rule:
            // - If TocEntryId is provided: pages optional (backend resolves from ToC)
            // - If TocEntryId is NOT provided: StartPage + EndPage required
            var hasToc = request.TocEntryId.HasValue && request.TocEntryId.Value > 0;

            if (!hasToc)
            {
                if (!request.StartPage.HasValue || request.StartPage.Value <= 0)
                    return BadRequest(new { message = "StartPage must be >= 1 when TocEntryId is not provided." });

                if (!request.EndPage.HasValue || request.EndPage.Value <= 0)
                    return BadRequest(new { message = "EndPage must be >= 1 when TocEntryId is not provided." });

                if (request.EndPage.Value < request.StartPage.Value)
                    return BadRequest(new { message = "EndPage must be >= StartPage." });
            }
            else
            {
                // If client did send pages anyway, validate them consistently (optional but nice)
                if (request.StartPage.HasValue && request.StartPage.Value <= 0)
                    return BadRequest(new { message = "StartPage must be >= 1 when provided." });

                if (request.EndPage.HasValue && request.EndPage.Value <= 0)
                    return BadRequest(new { message = "EndPage must be >= 1 when provided." });

                if (request.StartPage.HasValue && request.EndPage.HasValue && request.EndPage.Value < request.StartPage.Value)
                    return BadRequest(new { message = "EndPage must be >= StartPage." });
            }

            // normalize back (so downstream uses consistent Type)
            request.Type = type;

            var userId = GetUserIdIntOrThrow();

            try
            {
                var result = await _sectionSummarizer.SummarizeAsync(request, userId, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
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
