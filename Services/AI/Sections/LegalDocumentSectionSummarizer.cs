using LawAfrica.API.Data;
using LawAfrica.API.Models.Ai.Sections;
using LawAfrica.API.Models.DTOs.Ai.Sections;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Ai.Sections
{
    public class LegalDocumentSectionSummarizer : ILegalDocumentSectionSummarizer
    {
        private readonly ApplicationDbContext _db;

        // Bump when prompt changes
        private const string PROMPT_VERSION = "v1";

        // Basic quota (assumption for now)
        private const int DAILY_REQUEST_LIMIT = 30;

        // ✅ Hard safety clamps (tune later)
        private const int DEFAULT_MAX_PDF_PAGES_FALLBACK = 5000;
        private const int BASIC_MAX_SPAN_PAGES = 6;      // inclusive span
        private const int EXTENDED_MAX_SPAN_PAGES = 12;  // inclusive span

        public LegalDocumentSectionSummarizer(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<SectionSummaryResponseDto> SummarizeAsync(
            SectionSummaryRequestDto request,
            int userId,
            CancellationToken ct)
        {
            // Normalize type
            var type = string.IsNullOrWhiteSpace(request.Type)
                ? "basic"
                : request.Type.Trim().ToLowerInvariant();

            // ✅ Capture requested pages (raw)
            var requestedStart = request.StartPage;
            var requestedEnd = request.EndPage;

            // ✅ Normalize + clamp range (backend is final authority)
            var (startPage, endPage, warnings) = NormalizeAndClampRange(
                requestedStart,
                requestedEnd,
                type,
                GetMaxPdfPagesFallback()
            );

            var tocId = request.TocEntryId; // int? (optional)

            // 1) Cache lookup (unless force)
            if (!request.ForceRegenerate)
            {
                var cached = await _db.AiLegalDocumentSectionSummaries
                    .AsNoTracking()
                    .Where(x =>
                        x.UserId == userId &&
                        x.LegalDocumentId == request.LegalDocumentId &&
                        x.TocEntryId == tocId &&
                        x.StartPage == startPage &&
                        x.EndPage == endPage &&
                        x.Type == type &&
                        x.PromptVersion == PROMPT_VERSION)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (cached != null)
                {
                    return new SectionSummaryResponseDto
                    {
                        LegalDocumentId = request.LegalDocumentId,
                        TocEntryId = tocId,
                        Type = type,

                        RequestedStartPage = requestedStart,
                        RequestedEndPage = requestedEnd,

                        StartPage = startPage,
                        EndPage = endPage,

                        Summary = cached.Summary,
                        FromCache = true,
                        InputCharCount = 0, // filled later once we extract text
                        Warnings = warnings,
                        GeneratedAt = cached.CreatedAt
                    };
                }
            }

            // 2) Quota guardrail (only if not cached)
            await EnforceAndIncrementQuotaAsync(userId, ct);

            // 3) Generate summary (still stub for now)
            // Next step we’ll replace this with: extract text -> OpenAI -> summary.
            var output =
                $"[stub/{type}] Summary for doc #{request.LegalDocumentId}, toc #{tocId?.ToString() ?? "—"}, pages {startPage}-{endPage}.";

            // 4) Upsert cache row (keyed by SAFE effective pages)
            var existing = await _db.AiLegalDocumentSectionSummaries
                .Where(x =>
                    x.UserId == userId &&
                    x.LegalDocumentId == request.LegalDocumentId &&
                    x.TocEntryId == tocId &&
                    x.StartPage == startPage &&
                    x.EndPage == endPage &&
                    x.Type == type &&
                    x.PromptVersion == PROMPT_VERSION)
                .FirstOrDefaultAsync(ct);

            if (existing == null)
            {
                existing = new AiLegalDocumentSectionSummary
                {
                    UserId = userId,
                    LegalDocumentId = request.LegalDocumentId,
                    TocEntryId = tocId,
                    StartPage = startPage,
                    EndPage = endPage,
                    Type = type,
                    PromptVersion = PROMPT_VERSION,
                    Summary = output,
                    CreatedAt = DateTime.UtcNow
                };

                _db.AiLegalDocumentSectionSummaries.Add(existing);
            }
            else
            {
                existing.Summary = output;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);

            return new SectionSummaryResponseDto
            {
                LegalDocumentId = request.LegalDocumentId,
                TocEntryId = tocId,
                Type = type,

                RequestedStartPage = requestedStart,
                RequestedEndPage = requestedEnd,

                StartPage = startPage,
                EndPage = endPage,

                Summary = output,
                FromCache = false,
                InputCharCount = 0, // filled later once we extract text
                Warnings = warnings,
                GeneratedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// ✅ Strong normalization that never throws and always returns a safe effective range.
        /// </summary>
        private static (int start, int end, List<string> warnings) NormalizeAndClampRange(
            int requestedStart,
            int requestedEnd,
            string type,
            int maxPdfPages)
        {
            var warnings = new List<string>();

            // Defensive defaults
            var start = requestedStart;
            var end = requestedEnd;

            if (start <= 0)
            {
                start = 1;
                warnings.Add("StartPage was <= 0 and was set to 1.");
            }

            if (end <= 0)
            {
                end = start;
                warnings.Add("EndPage was <= 0 and was set to StartPage.");
            }

            if (end < start)
            {
                (start, end) = (end, start);
                if (start <= 0) start = 1; // keep safe even if both were bad
                warnings.Add("EndPage was < StartPage; values were swapped.");
            }

            // Clamp to [1..maxPdfPages]
            if (maxPdfPages <= 0) maxPdfPages = DEFAULT_MAX_PDF_PAGES_FALLBACK;

            if (start > maxPdfPages)
            {
                start = maxPdfPages;
                warnings.Add($"StartPage exceeded max page bound; clamped to {maxPdfPages}.");
            }

            if (end > maxPdfPages)
            {
                end = maxPdfPages;
                warnings.Add($"EndPage exceeded max page bound; clamped to {maxPdfPages}.");
            }

            if (end < start)
            {
                end = start;
                warnings.Add("After clamping, EndPage fell below StartPage; EndPage set to StartPage.");
            }

            // Max span guardrail (inclusive)
            var maxSpan = string.Equals(type, "extended", StringComparison.OrdinalIgnoreCase)
                ? EXTENDED_MAX_SPAN_PAGES
                : BASIC_MAX_SPAN_PAGES;

            var span = end - start + 1;
            if (span > maxSpan)
            {
                end = start + maxSpan - 1;
                if (end > maxPdfPages) end = maxPdfPages;

                warnings.Add($"Requested range was too large ({span} pages). " +
                             $"Clamped to max span of {maxSpan} pages: {start}-{end}.");
            }

            return (start, end, warnings);
        }

        /// <summary>
        /// For now we use a safe fallback bound.
        /// Later you can replace this with the real PDF page count lookup if/when you store it.
        /// </summary>
        private static int GetMaxPdfPagesFallback()
        {
            // Optional env override
            var raw = Environment.GetEnvironmentVariable("AI_PDF_MAX_PAGES");
            return int.TryParse(raw, out var n) && n > 0 ? n : DEFAULT_MAX_PDF_PAGES_FALLBACK;
        }

        private async Task EnforceAndIncrementQuotaAsync(int userId, CancellationToken ct)
        {
            var day = DateTime.UtcNow.Date;
            const string feature = "legal_doc_section_summary";

            var row = await _db.AiDailyAiUsages
                .Where(x => x.UserId == userId && x.DayUtc == day && x.Feature == feature)
                .FirstOrDefaultAsync(ct);

            if (row == null)
            {
                row = new AiDailyAiUsage
                {
                    UserId = userId,
                    DayUtc = day,
                    Feature = feature,
                    Requests = 0,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.AiDailyAiUsages.Add(row);
            }

            if (row.Requests >= DAILY_REQUEST_LIMIT)
                throw new InvalidOperationException($"Daily AI limit reached ({DAILY_REQUEST_LIMIT} requests).");

            row.Requests += 1;
            row.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
        }
    }
}
