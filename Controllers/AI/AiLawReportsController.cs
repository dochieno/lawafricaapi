using LawAfrica.API.Data;
using LawAfrica.API.DTOs.AI;
using LawAfrica.API.DTOs.AI.Commentary;
using LawAfrica.API.Models.Ai;
using LawAfrica.API.Services.Ai;
using LawAfrica.API.Services.Ai.Commentary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;


namespace LawAfrica.API.Controllers.Ai
{
    [ApiController]
    [Route("api/ai/law-reports")]
    [Authorize] // only logged in users can use AI
    public class AiLawReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILawReportSummarizer _summarizer;
        private readonly ILawReportRelatedCasesService _relatedCases;
        private readonly ILawReportChatService _chat;
        private readonly ILegalCommentaryAiService _commentary;

        public AiLawReportsController(
            ApplicationDbContext db,
            ILawReportSummarizer summarizer,
            ILawReportRelatedCasesService relatedCases,
            ILawReportChatService chat,
            ILegalCommentaryAiService commentary
        )
        {
            _db = db;
            _summarizer = summarizer;
            _relatedCases = relatedCases;
            _chat = chat;
            _commentary = commentary;
        }


        public class GenerateSummaryRequest
        {
            public string Type { get; set; } = "basic"; // "basic" | "extended"
            public bool ForceRegenerate { get; set; } = false;
        }

        [HttpGet("{id:int}/summary")]
        public async Task<IActionResult> GetSummary(int id, [FromQuery] string type = "basic", CancellationToken ct = default)
        {
            var userId = GetUserId();
            var summaryType = ParseType(type);

            var existing = await _db.AiLawReportSummaries
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.LawReportId == id && x.UserId == userId && x.SummaryType == summaryType, ct);

            if (existing == null)
                return NotFound(new { message = "No cached summary found. Generate one first." });

            return Ok(new
            {
                lawReportId = id,
                type = summaryType.ToString().ToLowerInvariant(),
                summary = existing.SummaryText,
                createdAt = existing.CreatedAt,
                updatedAt = existing.UpdatedAt
            });
        }

        [HttpPost("{id:int}/summary")]
        public async Task<IActionResult> GenerateSummary(int id, [FromBody] GenerateSummaryRequest req, CancellationToken ct)
        {
            req ??= new GenerateSummaryRequest();

            var userId = GetUserId();
            var summaryType = ParseType(req.Type);

            // 1) Load law report content
            var report = await _db.LawReports
                .AsNoTracking()
                .Include(x => x.LegalDocument)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (report == null)
                return NotFound(new { message = "Law report not found." });

            // ✅ NOTE: entitlement checks (premium/subscriptions) can be plugged in here later.
            // For now, we just require authentication (safe MVP).

            // 2) Cache check
            if (!req.ForceRegenerate)
            {
                var cached = await _db.AiLawReportSummaries
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.LawReportId == id && x.UserId == userId && x.SummaryType == summaryType, ct);

                if (cached != null)
                {
                    return Ok(new
                    {
                        lawReportId = id,
                        type = summaryType.ToString().ToLowerInvariant(),
                        summary = cached.SummaryText,
                        cached = true,
                        createdAt = cached.CreatedAt,
                        updatedAt = cached.UpdatedAt
                    });
                }
            }

            // 3) (Light) quota tracking stub (monthly)
            var periodKey = DateTime.UtcNow.ToString("yyyy-MM");
            var usage = await _db.AiUsages.FirstOrDefaultAsync(x => x.UserId == userId && x.PeriodKey == periodKey, ct);
            if (usage == null)
            {
                usage = new AiUsage { UserId = userId, PeriodKey = periodKey, SummariesGenerated = 0 };
                _db.AiUsages.Add(usage);
            }

            // Optional simple limit for now (you can tune later)
            var maxPerMonth = 50;
            if (usage.SummariesGenerated >= maxPerMonth)
                return StatusCode(429, new { message = $"AI summary limit reached for this month ({maxPerMonth})." });

            // 4) Call AI
            try
            {
                var content = report.ContentText ?? "";
                var (summary, modelUsed) = await _summarizer.SummarizeAsync(content, summaryType, ct);

                // 5) Upsert cache
                var existing = await _db.AiLawReportSummaries
                    .FirstOrDefaultAsync(x => x.LawReportId == id && x.UserId == userId && x.SummaryType == summaryType, ct);

                if (existing == null)
                {
                    existing = new AiLawReportSummary
                    {
                        LawReportId = id,
                        UserId = userId,
                        SummaryType = summaryType,
                        SummaryText = summary,
                        InputChars = Math.Min(content.Length, GetIntEnv("AI_MAX_INPUT_CHARS", 12000)),
                        OutputChars = summary.Length,
                        Model = modelUsed,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.AiLawReportSummaries.Add(existing);
                }
                else
                {
                    existing.SummaryText = summary;
                    existing.InputChars = Math.Min(content.Length, GetIntEnv("AI_MAX_INPUT_CHARS", 12000));
                    existing.OutputChars = summary.Length;
                    existing.Model = modelUsed;
                    existing.UpdatedAt = DateTime.UtcNow;
                }

                usage.SummariesGenerated += 1;
                usage.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);

                return Ok(new
                {
                    lawReportId = id,
                    type = summaryType.ToString().ToLowerInvariant(),
                    summary,
                    cached = false
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to generate AI summary.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        //Get Related Cases 
        [HttpPost("{id:int}/related-cases")]
        public async Task<IActionResult> GenerateRelatedCases(
        int id,
        [FromQuery] int takeKenya = 2,
        [FromQuery] int takeForeign = 2,
        CancellationToken ct = default
    )
            {
                var userId = GetUserId();

                var report = await _db.LawReports
                    .AsNoTracking()
                    .Include(x => x.LegalDocument)
                    .FirstOrDefaultAsync(x => x.Id == id, ct);

                if (report == null)
                    return NotFound(new { message = "Law report not found." });

                try
                {
                    var (items, modelUsed) = await _relatedCases.FindRelatedCasesAsync(report, takeKenya, takeForeign, ct);

                var currentTitle = (
                                    report.LegalDocument?.Title ??
                                    report.Parties ??
                                    ""
                                    ).Trim();
                var currentCitation = (report.Citation ?? "").Trim();

                    items = items
                        .Where(x =>
                            (x.LawReportId == null || x.LawReportId.Value != report.Id) &&
                            !LooksSameCase(x.Title, currentTitle) &&
                            (string.IsNullOrWhiteSpace(currentCitation) ||
                             !LooksSameCase(x.Citation, currentCitation))
                        )
                        .ToList();

                return Ok(new
                    {
                        lawReportId = id,
                        kenyaCount = items.Count(x => string.Equals(x.Jurisdiction, "Kenya", StringComparison.OrdinalIgnoreCase)),
                        foreignCount = items.Count(x => !string.Equals(x.Jurisdiction, "Kenya", StringComparison.OrdinalIgnoreCase)),
                        disclaimer = "AI suggestions may be inaccurate. Foreign cases are persuasive only. Always verify citations and holdings.",
                        model = modelUsed,
                        items
                    });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new
                    {
                        message = "Failed to generate AI related cases.",
                        detail = ex.Message,
                        type = ex.GetType().Name
                    });
                }
            }

        //Chat Controller:
        [HttpPost("{id:int}/chat")]
        public async Task<IActionResult> Chat(
            int id,
            [FromBody] LawReportChatRequestDto req,
            CancellationToken ct = default
        )
                {
                    req ??= new LawReportChatRequestDto();
                    var userId = GetUserId();

                    var report = await _db.LawReports
                        .AsNoTracking()
                        .Include(x => x.LegalDocument)
                        .FirstOrDefaultAsync(x => x.Id == id, ct);

                    if (report == null)
                        return NotFound(new { message = "Law report not found." });

                    var content = report.ContentText ?? "";
                    if (string.IsNullOrWhiteSpace(content))
                        return BadRequest(new { message = "This law report has no content to chat with yet." });

                    var userMessage = (req.Message ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(userMessage))
                        return BadRequest(new { message = "Message is required." });

                    var caseTitle = (
                        report.LegalDocument?.Title ??
                        report.Parties ??
                        report.CaseNumber ??   // if your LawReport has CaseNumber (your UI shows it)
                        "Law report"
                    ).Trim();

                    var caseCitation = (report.Citation ?? "").Trim();

                    try
                    {
                        var resp = await _chat.AskAsync(
                            id,
                            caseTitle,
                            caseCitation,
                            content,
                            userMessage,
                            req.History,
                            ct
                        );

                        // ✅ match frontend parsing: res.data.reply
                        return Ok(resp);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // good for quota/limits later
                        return StatusCode(429, new { message = ex.Message });
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, new
                        {
                            message = "Chat failed.",
                            detail = ex.Message,
                            type = ex.GetType().Name
                        });
                    }
                }

        // ------------------------------------------------------
        // ✅ TEMP TEST: POST /api/ai/law-reports/commentary/ask
        // Mirrors AiCommentaryController.Ask but under a known-working route.
        // ------------------------------------------------------
        [HttpPost("commentary/ask")]
        public async Task<IActionResult> CommentaryAsk(
            [FromBody] LegalCommentaryAskRequestDto req,
            CancellationToken ct = default)
        {
            req ??= new LegalCommentaryAskRequestDto();

            // AiLawReportsController uses string userId
            var userIdStr = GetUserId();

            // LegalCommentaryAiService expects int userId
            if (!int.TryParse(userIdStr, out var userId))
                return Unauthorized(new { message = "Invalid user id in token." });

            var tier = await GetUserAiTierAsync(ct); // "basic" | "extended"

            try
            {
                var resp = await _commentary.AskAsync(userId, req, tier, ct);
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

        [HttpPost("commentary/ask-stream")]
        public async Task CommentaryAskStream(
            [FromBody] LegalCommentaryAskRequestDto req,
            CancellationToken ct = default)
        {
            req ??= new LegalCommentaryAskRequestDto();

            var userIdStr = GetUserId();
            if (!int.TryParse(userIdStr, out var userId))
            {
                Response.Headers["Content-Type"] = "text/event-stream";
                Response.Headers["Cache-Control"] = "no-cache";
                await Response.WriteAsync("event: error\n", ct);
                await Response.WriteAsync("data: {\"message\":\"Unauthorized\"}\n\n", ct);
                await Response.Body.FlushAsync(ct);
                return;
            }

            var tier = await GetUserAiTierAsync(ct);

            Response.Headers["Content-Type"] = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no"; // helps some proxies

            async Task SendEventAsync(string type, object payload)
            {
                var json = JsonSerializer.Serialize(payload);
                await Response.WriteAsync($"event: {type}\n", ct);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            try
            {
                await foreach (var chunk in _commentary.AskStreamAsync(userId, req, tier, ct))
                {
                    if (!string.IsNullOrEmpty(chunk.DeltaText))
                        await SendEventAsync("delta", new { text = chunk.DeltaText });

                    if (chunk.Done)
                    {
                        await SendEventAsync("done", new { threadId = chunk.ThreadId });
                        break;
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                await SendEventAsync("error", new { message = ex.Message });
            }
            catch (Exception ex)
            {
                await SendEventAsync("error", new { message = "AI commentary failed.", detail = ex.Message });
            }
        }

        // Same tier logic you used in AiCommentaryController
        private async Task<string> GetUserAiTierAsync(CancellationToken ct)
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

        private string GetUserId()
        {
            // Works with Identity/JWT; fallback to "sub" if needed
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub")
                     ?? "";

            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("UserId not found in token.");

            return id;
        }

        private AiSummaryType ParseType(string? type)
        {
            var t = (type ?? "").Trim().ToLowerInvariant();
            return t == "extended" ? AiSummaryType.Extended : AiSummaryType.Basic;
        }

        private int GetIntEnv(string key, int fallback)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            return int.TryParse(raw, out var n) ? n : fallback;
        }

        // AiLawReportsController.cs (or similar)

        private static bool LooksSameCase(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;

            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

    }
}