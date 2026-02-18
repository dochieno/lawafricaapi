using LawAfrica.API.DTOs.AI.Commentary;
using LawAfrica.API.Services.Ai.Commentary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LawAfrica.API.Controllers.Ai
{
    [ApiController]
    [Route("api/ai/commentary")]
    [Authorize] // only logged in users can use AI
    public class LegalCommentaryController : ControllerBase
    {
        private readonly ILegalCommentaryAiService _commentary;

        public LegalCommentaryController(ILegalCommentaryAiService commentary)
        {
            _commentary = commentary;
        }

        /// <summary>
        /// General legal commentary AI:
        /// - NOT tied to a single LegalDocument
        /// - Grounds itself in LawAfrica DB first (law reports + pdf page texts)
        /// - May add general legal context (optional), but MUST remain legal-only
        /// - Includes disclaimer always
        /// </summary>
        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] LegalCommentaryAskRequestDto req, CancellationToken ct)
        {
            req ??= new LegalCommentaryAskRequestDto();

            var userId = GetUserIdInt();

            var q = (req.Question ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { message = "Question is required." });

            // Tier enforcement placeholder:
            // Replace this with your real subscription/tokens guard later.
            // For now: allow user to request mode, but service will enforce anyway.
            var userTier = ResolveUserTier();

            try
            {
                // NOTE: This assumes your ILegalCommentaryAiService signature includes userId.
                // If your current interface does not include userId yet, update it now:
                // AskAsync(int userId, LegalCommentaryAskRequestDto req, string userTier, CancellationToken ct)
                var resp = await _commentary.AskAsync(userId, req, userTier, ct);
                return Ok(resp);
            }
            catch (InvalidOperationException ex)
            {
                // Good for quota/limits later
                return StatusCode(429, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Legal commentary AI failed.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // --------------------------
        // Helpers
        // --------------------------

        private int GetUserIdInt()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub")
                   ?? "";

            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("UserId not found in token.");

            // If your tokens store user id as string GUID, change the service/resolver accordingly.
            if (!int.TryParse(raw, out var id))
                throw new InvalidOperationException($"Invalid UserId in token: '{raw}'");

            return id;
        }

        private string ResolveUserTier()
        {
            // ✅ Placeholder (keep simple for now):
            // You will later swap this with subscription/token logic.
            // Return "extended" when user is entitled; else "basic".

            // Example: if you have roles/claims for AI tiers:
            // if (User.IsInRole("AI_Extended") || User.HasClaim("ai_tier", "extended")) return "extended";

            return "basic";
        }
    }
}
