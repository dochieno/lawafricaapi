using LawAfrica.API.Data;
using LawAfrica.API.Models.Ai.Sections;
using LawAfrica.API.Models.DTOs.Ai.Sections;
using Microsoft.EntityFrameworkCore;
using OpenAI.Chat;

namespace LawAfrica.API.Services.Ai.Sections
{
    public class LegalDocumentSectionSummarizer : ILegalDocumentSectionSummarizer
    {
        private readonly ApplicationDbContext _db;
        private readonly ISectionTextExtractor _textExtractor;
        private readonly ChatClient _chatClient;
        private readonly IConfiguration _config;

        // Bump when prompt changes
        private const string PROMPT_VERSION = "v1";

        // Basic quota (assumption for now)
        private const int DAILY_REQUEST_LIMIT = 30;

        // ✅ Hard safety clamps (tune later)
        private const int DEFAULT_MAX_PDF_PAGES_FALLBACK = 5000;
        private const int BASIC_MAX_SPAN_PAGES = 6;      // inclusive span
        private const int EXTENDED_MAX_SPAN_PAGES = 12;  // inclusive span

        public LegalDocumentSectionSummarizer(
            ApplicationDbContext db,
            ISectionTextExtractor textExtractor,
            ChatClient chatClient,
            IConfiguration config)
        {
            _db = db;
            _textExtractor = textExtractor;
            _chatClient = chatClient;
            _config = config;
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

            // ✅ Raw request pages (nullable now)
            var rawRequestedStart = request.StartPage; // int?
            var rawRequestedEnd = request.EndPage;     // int?

            // ✅ What we will actually normalize/clamp (may come from ToC)
            int? requestedStart = rawRequestedStart;
            int? requestedEnd = rawRequestedEnd;

            var tocId = request.TocEntryId; // int? (optional)
            var warnings = new List<string>();

            // ✅ If ToC entry is provided, resolve pages from DB (preferred path)
            if (tocId.HasValue && tocId.Value > 0)
            {
                var (tocStart, tocEnd, tocWarn) = await TryResolveRangeFromTocAsync(
                    request.LegalDocumentId,
                    tocId.Value,
                    ct);

                if (!string.IsNullOrWhiteSpace(tocWarn))
                    warnings.Add(tocWarn);

                if (tocStart.HasValue)
                {
                    requestedStart = tocStart;
                    requestedEnd = tocEnd ?? tocStart;
                    warnings.Add("Page range was resolved from ToC entry.");
                }
                else
                {
                    warnings.Add("Could not resolve pages from ToC entry; falling back to request pages if provided.");
                }
            }

            // ✅ Normalize + clamp (backend is final authority)
            var (startPage, endPage, clampWarnings) = NormalizeAndClampRange(
                requestedStart,
                requestedEnd,
                type,
                GetMaxPdfPagesFallback()
            );

            warnings.AddRange(clampWarnings);

            // ✅ For response: keep "raw request" as what client sent (or 0 when missing)
            var responseRequestedStart = rawRequestedStart ?? 0;
            var responseRequestedEnd = rawRequestedEnd ?? 0;

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

                        RequestedStartPage = responseRequestedStart,
                        RequestedEndPage = responseRequestedEnd,

                        StartPage = startPage,
                        EndPage = endPage,

                        Summary = cached.Summary,
                        FromCache = true,

                        // ✅ Option B: return stored InputCharCount from cache table
                        InputCharCount = cached.InputCharCount ?? 0,

                        Warnings = warnings,
                        GeneratedAt = cached.CreatedAt
                    };
                }
            }

            // 2) Quota guardrail (only if not cached)
            await EnforceAndIncrementQuotaAsync(userId, ct);

            // 3) Extract stored text for pages (DB)
            var extraction = await _textExtractor.ExtractAsync(
                request.LegalDocumentId,
                startPage,
                endPage,
                ct);

            // Add warnings for missing pages (cap list so it doesn’t explode)
            if (extraction.MissingPages.Count > 0)
            {
                var cap = 30;
                var shown = extraction.MissingPages.Take(cap).ToList();
                var suffix = extraction.MissingPages.Count > cap
                    ? $" … (+{extraction.MissingPages.Count - cap} more)"
                    : "";
                warnings.Add($"Missing stored text for pages: {string.Join(", ", shown)}{suffix}.");
            }

            // If empty text, return safe message and cache it
            if (string.IsNullOrWhiteSpace(extraction.Text))
            {
                warnings.Add("No extracted text was found for this section. Index the document text to enable summaries.");

                var emptyOutput = "No content was found for this section.";

                await UpsertCacheAsync(
                    userId,
                    request.LegalDocumentId,
                    tocId,
                    startPage,
                    endPage,
                    type,
                    emptyOutput,
                    inputCharCount: 0,
                    tokensIn: null,
                    tokensOut: null,
                    ct);

                return new SectionSummaryResponseDto
                {
                    LegalDocumentId = request.LegalDocumentId,
                    TocEntryId = tocId,
                    Type = type,

                    RequestedStartPage = responseRequestedStart,
                    RequestedEndPage = responseRequestedEnd,

                    StartPage = startPage,
                    EndPage = endPage,

                    Summary = emptyOutput,
                    FromCache = false,
                    InputCharCount = 0,
                    Warnings = warnings,
                    GeneratedAt = DateTime.UtcNow
                };
            }

            // =========================================================
            // ✅ Prepare "effective" text for the LLM (truncate by char budget)
            // effectiveCharCount MUST reflect the final text passed to OpenAI.
            // =========================================================
            var effectiveText = (extraction.Text ?? "").Trim();

            var maxInputChars = GetInt(
                string.Equals(type, "extended", StringComparison.OrdinalIgnoreCase)
                    ? "AI_SECTION_MAX_INPUT_CHARS_EXTENDED"
                    : "AI_SECTION_MAX_INPUT_CHARS_BASIC",
                string.Equals(type, "extended", StringComparison.OrdinalIgnoreCase) ? 24000 : 12000
            );

            if (effectiveText.Length > maxInputChars)
            {
                effectiveText = effectiveText.Substring(0, maxInputChars);
                warnings.Add($"Input text was truncated to {maxInputChars} characters.");
            }

            var effectiveCharCount = effectiveText.Length;

            // =========================================================
            // ✅ OpenAI call (real summary)
            // =========================================================
            var maxOutputTokens = GetInt(
                string.Equals(type, "extended", StringComparison.OrdinalIgnoreCase)
                    ? "AI_SECTION_MAX_OUTPUT_TOKENS_EXTENDED"
                    : "AI_SECTION_MAX_OUTPUT_TOKENS_BASIC",
                string.Equals(type, "extended", StringComparison.OrdinalIgnoreCase) ? 700 : 350
            );

            var system = BuildSystemPrompt(type);

            // Provide light metadata context to help the model stay grounded
            var header =
                $"Document #{request.LegalDocumentId}\n" +
                $"ToC: {(tocId.HasValue ? tocId.Value.ToString() : "—")}\n" +
                $"Pages: {startPage}-{endPage}\n" +
                (!string.IsNullOrWhiteSpace(request.SectionTitle) ? $"Title: {request.SectionTitle}\n" : "");

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(system),
                new UserChatMessage(header + "\nSECTION TEXT (source of truth):\n" + effectiveText)
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = maxOutputTokens,
                Temperature = 0.2f
            };

            ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, options, ct);

            var output = completion?.Content?.FirstOrDefault()?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException("AI returned an empty summary.");

            // Token usage (defensive: SDK versions differ)
            int? tokensIn = null;
            int? tokensOut = null;
            try
            {
                tokensIn = completion?.Usage?.InputTokenCount;
                tokensOut = completion?.Usage?.OutputTokenCount;
            }
            catch
            {
                // ignore
            }

            await UpsertCacheAsync(
                userId,
                request.LegalDocumentId,
                tocId,
                startPage,
                endPage,
                type,
                output,
                inputCharCount: effectiveCharCount,
                tokensIn: tokensIn,
                tokensOut: tokensOut,
                ct);

            return new SectionSummaryResponseDto
            {
                LegalDocumentId = request.LegalDocumentId,
                TocEntryId = tocId,
                Type = type,

                RequestedStartPage = responseRequestedStart,
                RequestedEndPage = responseRequestedEnd,

                StartPage = startPage,
                EndPage = endPage,

                Summary = output,
                FromCache = false,
                InputCharCount = effectiveCharCount,
                Warnings = warnings,
                GeneratedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// ✅ Resolve pages from ToC entry.
        /// Uses your model: LegalDocumentTocEntry (StartPage/EndPage).
        /// </summary>
        private async Task<(int? start, int? end, string? warning)> TryResolveRangeFromTocAsync(
            int legalDocumentId,
            int tocEntryId,
            CancellationToken ct)
        {
            var toc = await _db.LegalDocumentTocEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.LegalDocumentId == legalDocumentId &&
                    x.Id == tocEntryId,
                    ct);

            if (toc == null)
                return (null, null, "ToC entry was not found for this document.");

            // Only support PageRange here (Anchor is not page-based)
            // If you want: if toc.TargetType == TocTargetType.Anchor => return warning.
            var start = toc.StartPage;
            var end = toc.EndPage;

            if (!start.HasValue || start.Value <= 0)
                return (null, null, "ToC entry has no StartPage mapping.");

            if (!end.HasValue || end.Value <= 0)
                end = start;

            if (end.Value < start.Value)
                (start, end) = (end, start);

            return (start, end, null);
        }

        private async Task UpsertCacheAsync(
            int userId,
            int legalDocumentId,
            int? tocId,
            int startPage,
            int endPage,
            string type,
            string summary,
            int? inputCharCount,
            int? tokensIn,
            int? tokensOut,
            CancellationToken ct)
        {
            var existing = await _db.AiLegalDocumentSectionSummaries
                .Where(x =>
                    x.UserId == userId &&
                    x.LegalDocumentId == legalDocumentId &&
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
                    LegalDocumentId = legalDocumentId,
                    TocEntryId = tocId,
                    StartPage = startPage,
                    EndPage = endPage,
                    Type = type,
                    PromptVersion = PROMPT_VERSION,
                    Summary = summary,
                    InputCharCount = inputCharCount,
                    TokensIn = tokensIn,
                    TokensOut = tokensOut,
                    CreatedAt = DateTime.UtcNow
                };

                _db.AiLegalDocumentSectionSummaries.Add(existing);
            }
            else
            {
                existing.Summary = summary;
                existing.InputCharCount = inputCharCount;
                existing.TokensIn = tokensIn;
                existing.TokensOut = tokensOut;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// ✅ Strong normalization that never throws and always returns a safe effective range.
        /// Supports nullable requestedStart/requestedEnd.
        /// </summary>
        private static (int start, int end, List<string> warnings) NormalizeAndClampRange(
            int? requestedStart,
            int? requestedEnd,
            string type,
            int maxPdfPages)
        {
            var warnings = new List<string>();

            // Defensive defaults
            var start = requestedStart ?? 0;
            var end = requestedEnd ?? 0;

            if (start <= 0)
            {
                start = 1;
                warnings.Add("StartPage was missing/invalid and was set to 1.");
            }

            if (end <= 0)
            {
                end = start;
                warnings.Add("EndPage was missing/invalid and was set to StartPage.");
            }

            if (end < start)
            {
                (start, end) = (end, start);
                if (start <= 0) start = 1;
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

        private static int GetMaxPdfPagesFallback()
        {
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

        private int GetInt(string key, int fallback)
        {
            var raw = _config[key];
            return int.TryParse(raw, out var n) ? n : fallback;
        }

        private static string BuildSystemPrompt(string type)
        {
            if (string.Equals(type, "extended", StringComparison.OrdinalIgnoreCase))
            {
                return
                @"You are a legal summarization assistant.
                You MUST summarize ONLY the text provided by the user. Do not add outside facts or citations.
                If something is not clearly stated, write: ""Not stated in the section.""

                Return the summary in this structure:

                OVERVIEW: 2–4 sentences
                KEY POINTS: 5–10 bullet points
                IMPORTANT TERMS: bullets (if any)
                PRACTICAL TAKEAWAYS: 3–6 bullet points

                Be concise and clear.";
                            }

                            return
                @"You are a legal summarization assistant.
                Summarize ONLY the text provided by the user. Do not add outside facts or citations.
                If something is unclear, say: ""Not stated in the section.""

                Output:
                - A 1–2 paragraph summary
                - Then 3–6 key bullet points.";
        }
    }
}
