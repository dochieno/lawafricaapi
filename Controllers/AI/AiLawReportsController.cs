using System.Security.Claims;
using LawAfrica.API.Data;
using LawAfrica.API.Models.Ai;
using LawAfrica.API.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Ai
{
    [ApiController]
    [Route("api/ai/law-reports")]
    [Authorize] // only logged in users can use AI
    public class AiLawReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILawReportSummarizer _summarizer;

        public AiLawReportsController(ApplicationDbContext db, ILawReportSummarizer summarizer)
        {
            _db = db;
            _summarizer = summarizer;
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
    }
}