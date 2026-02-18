// =======================================================
// FILE: LawAfrica.API/Controllers/Ai/AiCommentaryControllerV2.cs
// Purpose:
// - AI Commentary endpoints (ask + threads)
// - Styled like AiLawReportsController (GetUserId + consistent responses)
// Routes:
//   POST   /api/ai/commentary/ask
//   GET    /api/ai/commentary/threads
//   GET    /api/ai/commentary/threads/{threadId}
//   POST   /api/ai/commentary/threads/{threadId}/delete
// =======================================================

using LawAfrica.API.Data;
using LawAfrica.API.DTOs.AI.Commentary;
using LawAfrica.API.Models.Ai.Commentary;
using LawAfrica.API.Services.Ai.Commentary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LawAfrica.API.Controllers.Ai
{
    [ApiController]
    [Route("api/ai/commentary")]
    [Authorize]
    public class AiCommentaryControllerV2 : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILegalCommentaryAiService _ai;

        public AiCommentaryControllerV2(ApplicationDbContext db, ILegalCommentaryAiService ai)
        {
            _db = db;
            _ai = ai;
        }

        // -------------------------
        // POST /api/ai/commentary/ask
        // -------------------------
        [HttpPost("ask")]
        public async Task<IActionResult> Ask(
            [FromBody] LegalCommentaryAskRequestDto req,
            CancellationToken ct = default)
        {
            req ??= new LegalCommentaryAskRequestDto();

            var userId = GetUserId();
            var tier = await GetUserTierAsync(ct); // "basic" | "extended"

            try
            {
                // Service already handles: create/load thread, persist msgs, retrieval, etc.
                var resp = await _ai.AskAsync(userId, req, tier, ct);
                return Ok(resp);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "AI commentary failed.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // GET /api/ai/commentary/threads?take=30&skip=0
        // -------------------------
        [HttpGet("threads")]
        public async Task<IActionResult> Threads(
            [FromQuery] int take = 30,
            [FromQuery] int skip = 0,
            CancellationToken ct = default)
        {
            var userId = GetUserId();

            take = Math.Clamp(take, 1, 200);
            skip = Math.Max(0, skip);

            var items = await _db.AiCommentaryThreads
                .AsNoTracking()
                .Where(x => x.UserId == userId && !x.IsDeleted)
                .OrderByDescending(x => x.LastActivityAtUtc)
                .Skip(skip)
                .Take(take)
                .Select(x => new
                {
                    threadId = x.Id,
                    title = x.Title,
                    mode = x.Mode,
                    countryName = x.CountryName,
                    countryIso = x.CountryIso,
                    regionLabel = x.RegionLabel,
                    lastModel = x.LastModel,
                    createdAtUtc = x.CreatedAtUtc,
                    lastActivityAtUtc = x.LastActivityAtUtc
                })
                .ToListAsync(ct);

            var total = await _db.AiCommentaryThreads
                .AsNoTracking()
                .Where(x => x.UserId == userId && !x.IsDeleted)
                .CountAsync(ct);

            return Ok(new { total, take, skip, items });
        }

        // -------------------------
        // GET /api/ai/commentary/threads/{threadId}?takeMessages=80
        // -------------------------
        [HttpGet("threads/{threadId:long}")]
        public async Task<IActionResult> Thread(
            long threadId,
            [FromQuery] int takeMessages = 80,
            CancellationToken ct = default)
        {
            var userId = GetUserId();
            takeMessages = Math.Clamp(takeMessages, 1, 500);

            var thread = await _db.AiCommentaryThreads
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == threadId && x.UserId == userId && !x.IsDeleted, ct);

            if (thread == null)
                return NotFound(new { message = "Thread not found." });

            var msgs = await _db.AiCommentaryMessages
                .AsNoTracking()
                .Where(x => x.ThreadId == threadId && !x.IsDeleted)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(takeMessages)
                .Select(x => new
                {
                    messageId = x.Id,
                    role = x.Role,
                    mode = x.Mode,
                    model = x.Model,
                    disclaimerVersion = x.DisclaimerVersion,
                    contentMarkdown = x.ContentMarkdown,
                    createdAtUtc = x.CreatedAtUtc
                })
                .ToListAsync(ct);

            msgs.Reverse();

            var assistantIds = msgs
                .Where(m => string.Equals(m.role, "assistant", StringComparison.OrdinalIgnoreCase))
                .Select(m => (long)m.messageId)
                .ToList();

            var sourcesByMessage = new Dictionary<long, List<object>>();

            if (assistantIds.Count > 0)
            {
                var sources = await _db.AiCommentaryMessageSources
                    .AsNoTracking()
                    .Where(s => assistantIds.Contains(s.MessageId))
                    .OrderByDescending(s => s.Score)
                    .Select(s => new
                    {
                        messageId = s.MessageId,
                        type = s.Type,
                        score = s.Score,
                        title = s.Title,
                        citation = s.Citation,
                        snippet = s.Snippet,
                        lawReportId = s.LawReportId,
                        legalDocumentId = s.LegalDocumentId,
                        pageNumber = s.PageNumber,
                        linkUrl = s.LinkUrl
                    })
                    .ToListAsync(ct);

                foreach (var s in sources)
                {
                    if (!sourcesByMessage.TryGetValue(s.messageId, out var list))
                    {
                        list = new List<object>();
                        sourcesByMessage[s.messageId] = list;
                    }
                    list.Add(s);
                }
            }

            return Ok(new
            {
                thread = new
                {
                    threadId = thread.Id,
                    title = thread.Title,
                    mode = thread.Mode,
                    countryName = thread.CountryName,
                    countryIso = thread.CountryIso,
                    regionLabel = thread.RegionLabel,
                    lastModel = thread.LastModel,
                    createdAtUtc = thread.CreatedAtUtc,
                    lastActivityAtUtc = thread.LastActivityAtUtc
                },
                messages = msgs.Select(m => new
                {
                    m.messageId,
                    m.role,
                    m.mode,
                    m.model,
                    m.disclaimerVersion,
                    m.contentMarkdown,
                    m.createdAtUtc,
                    sources = sourcesByMessage.TryGetValue(m.messageId, out var src) ? src : new List<object>()
                })
            });
        }

        // -------------------------
        // POST /api/ai/commentary/threads/{threadId}/delete
        // -------------------------
        [HttpPost("threads/{threadId:long}/delete")]
        public async Task<IActionResult> DeleteThread(long threadId, CancellationToken ct = default)
        {
            var userId = GetUserId();

            var thread = await _db.AiCommentaryThreads
                .FirstOrDefaultAsync(x => x.Id == threadId && x.UserId == userId && !x.IsDeleted, ct);

            if (thread == null)
                return NotFound(new { message = "Thread not found." });

            thread.IsDeleted = true;
            thread.DeletedAtUtc = DateTime.UtcNow;
            thread.LastActivityAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return Ok(new { threadId, deleted = true });
        }

        // -------------------------
        // Helpers (match AiLawReportsController style)
        // -------------------------
        private int GetUserId()
        {
            var raw =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                "";

            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("UserId not found in token.");

            // Your LegalCommentaryAiService expects int userId
            if (!int.TryParse(raw, out var id))
                throw new InvalidOperationException("UserId in token is not a valid integer.");

            return id;
        }

        private async Task<string> GetUserTierAsync(CancellationToken ct)
        {
            var claimTier = (User.FindFirst("aiTier")?.Value ?? User.FindFirst("tier")?.Value ?? "")
                .Trim()
                .ToLowerInvariant();

            if (claimTier == "extended" || claimTier == "basic")
                return claimTier;

            if (User.IsInRole("Admin"))
                return "extended";

            await Task.CompletedTask;
            return "basic";
        }
    }
}
