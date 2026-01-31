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

        public LegalDocumentSectionSummarizer(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<SectionSummaryResponseDto> SummarizeAsync(
            SectionSummaryRequestDto request,
            int userId,
            CancellationToken ct)
        {
            // Normalize inputs (service assumes controller validated DTO)
            var type = string.IsNullOrWhiteSpace(request.Type)
                ? "basic"
                : request.Type.Trim().ToLowerInvariant();

            var startPage = request.StartPage;
            var endPage = request.EndPage;

            // 1) Cache lookup (unless force)
            if (!request.ForceRegenerate)
            {
                var cached = await _db.AiLegalDocumentSectionSummaries
                    .AsNoTracking()
                    .Where(x =>
                        x.UserId == userId &&
                        x.LegalDocumentId == request.LegalDocumentId &&
                        x.TocEntryId == request.TocEntryId &&
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
                        TocEntryId = request.TocEntryId,
                        Type = type,
                        StartPage = startPage,
                        EndPage = endPage,
                        Summary = cached.Summary,
                        FromCache = true,
                        InputCharCount = 0, // will be filled once we extract text
                        GeneratedAt = cached.CreatedAt
                    };
                }
            }

            // 2) Quota guardrail (only if not cached)
            await EnforceAndIncrementQuotaAsync(userId, ct);

            // 3) Generate summary (still stub for now)
            // Next step we’ll replace this with: extract text -> OpenAI -> summary.
            var output =
                $"[stub/{type}] Summary for doc #{request.LegalDocumentId}, toc #{request.TocEntryId}, pages {startPage}-{endPage}.";

            // 4) Upsert cache row
            var existing = await _db.AiLegalDocumentSectionSummaries
                .Where(x =>
                    x.UserId == userId &&
                    x.LegalDocumentId == request.LegalDocumentId &&
                    x.TocEntryId == request.TocEntryId &&
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
                    TocEntryId = request.TocEntryId,
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
                TocEntryId = request.TocEntryId,
                Type = type,
                StartPage = startPage,
                EndPage = endPage,
                Summary = output,
                FromCache = false,
                InputCharCount = 0, // will be filled once we extract text
                GeneratedAt = DateTime.UtcNow
            };
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
