// =======================================================
// FILE: Services/Ai/Sections/LegalDocumentSectionSummarizer.cs
// Purpose: Summarize a legal document section (range or ToC-based) with
//          global+content-hash caching and per-user quota enforcement.
// Notes:
// - Cache is NOT per-user. CreatedByUserId is audit only.
// - Text source is DB: LegalDocumentPageTexts (via ISectionTextExtractor).
// - Cache key uses OwnerKey + type + promptVersion + contentHash.
// =======================================================

using System.Security.Cryptography;
using System.Text;
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

        // Bump when prompt changes (default fallback if request.PromptVersion is null)
        private const string PROMPT_VERSION = "v1";

        // Basic quota (assumption for now)
        private const int DAILY_REQUEST_LIMIT = 30;

        // Hard safety clamps (tune later)
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

        /// <summary>
        /// Main entry: resolves effective page range, extracts text, checks global cache by content hash,
        /// enforces per-user quota on cache miss, calls OpenAI, and upserts cache.
        /// </summary>
        public async Task<SectionSummaryResponseDto> SummarizeAsync(
            SectionSummaryRequestDto request,
            int userId,
            CancellationToken ct)
        {
            // Normalize type
            var type = string.IsNullOrWhiteSpace(request.Type)
                ? "basic"
                : request.Type.Trim().ToLowerInvariant();

            // Prompt version: allow caller to pass one to intentionally bust/partition cache
            var promptVersion = string.IsNullOrWhiteSpace(request.PromptVersion)
                ? PROMPT_VERSION
                : request.PromptVersion.Trim();

            var rawRequestedStart = request.StartPage; // nullable
            var rawRequestedEnd = request.EndPage;     // nullable

            int? requestedStart = rawRequestedStart;
            int? requestedEnd = rawRequestedEnd;

            var tocId = request.TocEntryId;
            var warnings = new List<string>();

            // Resolve via ToC if possible
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

            // Normalize and clamp final page range (backend is authority)
            var (startPage, endPage, clampWarnings) = NormalizeAndClampRange(
                requestedStart,
                requestedEnd,
                type,
                GetMaxPdfPagesFallback());

            warnings.AddRange(clampWarnings);

            // For response: echo what client asked (nullable)
            var responseRequestedStart = rawRequestedStart;
            var responseRequestedEnd = rawRequestedEnd;

            // OwnerKey is the section identity used by cache + UI verification
            var ownerKey = BuildOwnerKey(request.LegalDocumentId, tocId, startPage, endPage);

            // 1) Extract stored text (DB) FIRST so we can hash it
            var extraction = await _textExtractor.ExtractAsync(
                request.LegalDocumentId,
                startPage,
                endPage,
                ct);

            if (extraction.MissingPages.Count > 0)
            {
                var cap = 30;
                var shown = extraction.MissingPages.Take(cap).ToList();
                var suffix = extraction.MissingPages.Count > cap
                    ? $" … (+{extraction.MissingPages.Count - cap} more)"
                    : "";
                warnings.Add($"Missing stored text for pages: {string.Join(", ", shown)}{suffix}.");
            }

            // 2) Build effective text (truncate) — this is what we hash & send to OpenAI
            var effectiveText = (extraction.Text ?? string.Empty).Trim();

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

            // Hash must reflect the exact content sent to OpenAI (or empty)
            var contentHash = ComputeSha256Hex(effectiveText);

            // CacheKey must fit [MaxLength(240)]
            var cacheKey = BuildCacheKey(ownerKey, type, promptVersion, contentHash);

            // Empty case: return safe message (and optionally cache it)
            if (string.IsNullOrWhiteSpace(effectiveText))
            {
                warnings.Add("No extracted text was found for this section. Index the document text to enable summaries.");

                var emptyOutput = "No content was found for this section.";

                await UpsertCacheByHashAsync(
                    createdByUserId: userId,
                    legalDocumentId: request.LegalDocumentId,
                    tocId: tocId,
                    startPage: startPage,
                    endPage: endPage,
                    type: type,
                    promptVersion: promptVersion,
                    ownerKey: ownerKey,
                    contentHash: contentHash,
                    cacheKey: cacheKey,
                    sectionTitle: request.SectionTitle,
                    summary: emptyOutput,
                    inputCharCount: 0,
                    tokensIn: null,
                    tokensOut: null,
                    modelUsed: null,
                    ct: ct);

                return new SectionSummaryResponseDto
                {
                    LegalDocumentId = request.LegalDocumentId,
                    TocEntryId = tocId,
                    Type = type,

                    RequestedStartPage = responseRequestedStart,
                    RequestedEndPage = responseRequestedEnd,

                    StartPage = startPage,
                    EndPage = endPage,

                    SectionTitle = request.SectionTitle,

                    OwnerKey = ownerKey,
                    ContentHash = contentHash,
                    CacheKey = cacheKey,

                    Summary = emptyOutput,
                    FromCache = false,
                    InputCharCount = 0,
                    Warnings = warnings,

                    ModelUsed = null,
                    PromptVersion = promptVersion,
                    GeneratedAt = DateTime.UtcNow
                };
            }

            // 3) Cache lookup by (OwnerKey + type + promptVersion + contentHash) (NOT per-user)
            if (!request.ForceRegenerate)
            {
                var cached = await _db.AiLegalDocumentSectionSummaries
                    .AsNoTracking()
                    .Where(x =>
                        x.OwnerKey == ownerKey &&
                        x.LegalDocumentId == request.LegalDocumentId &&
                        x.TocEntryId == tocId &&
                        x.StartPage == startPage &&
                        x.EndPage == endPage &&
                        x.Type == type &&
                        x.PromptVersion == promptVersion &&
                        x.ContentHash == contentHash)
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

                        SectionTitle = cached.SectionTitle ?? request.SectionTitle,

                        OwnerKey = cached.OwnerKey,
                        ContentHash = cached.ContentHash,
                        CacheKey = cached.CacheKey,

                        Summary = cached.Summary,
                        FromCache = true,
                        InputCharCount = cached.InputCharCount ?? effectiveCharCount,
                        Warnings = warnings,

                        ModelUsed = cached.ModelUsed,
                        PromptVersion = cached.PromptVersion,
                        GeneratedAt = cached.CreatedAt
                    };
                }
            }

            // 4) Quota only on cache miss
            await EnforceAndIncrementQuotaAsync(userId, ct);

            // 5) OpenAI call
            var maxOutputTokens = GetInt(
                string.Equals(type, "extended", StringComparison.OrdinalIgnoreCase)
                    ? "AI_SECTION_MAX_OUTPUT_TOKENS_EXTENDED"
                    : "AI_SECTION_MAX_OUTPUT_TOKENS_BASIC",
                string.Equals(type, "extended", StringComparison.OrdinalIgnoreCase) ? 700 : 350
            );

            var system = BuildSystemPrompt(type);

            // Light metadata header (helps keep the model grounded)
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

            int? tokensIn = null;
            int? tokensOut = null;
            try
            {
                tokensIn = completion?.Usage?.InputTokenCount;
                tokensOut = completion?.Usage?.OutputTokenCount;
            }
            catch
            {
                // ignore SDK differences
            }

            // Best-effort model name (SDK-dependent; keep null-safe)
            string? modelUsed = null;
            try
            {
                // Some SDKs expose completion.Model, others don't.
                var modelProp = completion?.GetType().GetProperty("Model");
                modelUsed = modelProp?.GetValue(completion)?.ToString();
            }
            catch { }

            // 6) Cache upsert (global by content hash + section identity)
            await UpsertCacheByHashAsync(
                createdByUserId: userId,
                legalDocumentId: request.LegalDocumentId,
                tocId: tocId,
                startPage: startPage,
                endPage: endPage,
                type: type,
                promptVersion: promptVersion,
                ownerKey: ownerKey,
                contentHash: contentHash,
                cacheKey: cacheKey,
                sectionTitle: request.SectionTitle,
                summary: output,
                inputCharCount: effectiveCharCount,
                tokensIn: tokensIn,
                tokensOut: tokensOut,
                modelUsed: modelUsed,
                ct: ct);

            return new SectionSummaryResponseDto
            {
                LegalDocumentId = request.LegalDocumentId,
                TocEntryId = tocId,
                Type = type,

                RequestedStartPage = responseRequestedStart,
                RequestedEndPage = responseRequestedEnd,

                StartPage = startPage,
                EndPage = endPage,

                SectionTitle = request.SectionTitle,

                OwnerKey = ownerKey,
                ContentHash = contentHash,
                CacheKey = cacheKey,

                Summary = output,
                FromCache = false,
                InputCharCount = effectiveCharCount,
                Warnings = warnings,

                ModelUsed = modelUsed,
                PromptVersion = promptVersion,
                GeneratedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Resolves start/end pages from a LegalDocumentTocEntry (if mapped to pages).
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

        /// <summary>
        /// Normalizes/clamps page range safely (never throws), with max-span guardrails.
        /// </summary>
        private static (int start, int end, List<string> warnings) NormalizeAndClampRange(
            int? requestedStart,
            int? requestedEnd,
            string type,
            int maxPdfPages)
        {
            var warnings = new List<string>();

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
        /// Reads max page fallback from env var AI_PDF_MAX_PAGES (safe default if missing).
        /// </summary>
        private static int GetMaxPdfPagesFallback()
        {
            var raw = Environment.GetEnvironmentVariable("AI_PDF_MAX_PAGES");
            return int.TryParse(raw, out var n) && n > 0 ? n : DEFAULT_MAX_PDF_PAGES_FALLBACK;
        }

        /// <summary>
        /// Enforces daily per-user request limit (only called on cache miss).
        /// </summary>
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

        /// <summary>
        /// Reads integer config values from IConfiguration with a fallback.
        /// </summary>
        private int GetInt(string key, int fallback)
        {
            var raw = _config[key];
            return int.TryParse(raw, out var n) ? n : fallback;
        }

        /// <summary>
        /// Builds the system prompt for "basic" vs "extended" output format.
        /// </summary>
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

        /// <summary>
        /// Computes SHA256 hex (lowercase) for cache integrity.
        /// </summary>
        private static string ComputeSha256Hex(string input)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Builds OwnerKey for UI + cache identity (ToC-based preferred, else range-based).
        /// </summary>
        private static string BuildOwnerKey(int legalDocumentId, int? tocId, int startPage, int endPage)
        {
            return tocId.HasValue && tocId.Value > 0
                ? $"doc:{legalDocumentId}|toc:{tocId.Value}"
                : $"doc:{legalDocumentId}|range:{startPage}-{endPage}";
        }

        /// <summary>
        /// Builds CacheKey (max 240 chars). If it ever grows, we keep it deterministic.
        /// </summary>
        private static string BuildCacheKey(string ownerKey, string type, string promptVersion, string contentHash)
        {
            // Example:
            // doc:12|toc:99|type:basic|pv:v1|hash:<64>
            var key = $"{ownerKey}|type:{type}|pv:{promptVersion}|hash:{contentHash}";

            // Ensure it fits the model constraint.
            if (key.Length <= 240) return key;

            // Deterministic fallback: hash the oversized key and store compact form.
            var compact = ComputeSha256Hex(key);
            return $"{ownerKey}|k:{compact}";
        }

        /// <summary>
        /// Upserts a cached summary by global content-hash identity (NOT per-user).
        /// CreatedByUserId is audit only.
        /// </summary>
        private async Task UpsertCacheByHashAsync(
            int createdByUserId,
            int legalDocumentId,
            int? tocId,
            int startPage,
            int endPage,
            string type,
            string promptVersion,
            string ownerKey,
            string contentHash,
            string cacheKey,
            string? sectionTitle,
            string summary,
            int? inputCharCount,
            int? tokensIn,
            int? tokensOut,
            string? modelUsed,
            CancellationToken ct)
        {
            var existing = await _db.AiLegalDocumentSectionSummaries
                .Where(x =>
                    x.OwnerKey == ownerKey &&
                    x.LegalDocumentId == legalDocumentId &&
                    x.TocEntryId == tocId &&
                    x.StartPage == startPage &&
                    x.EndPage == endPage &&
                    x.Type == type &&
                    x.PromptVersion == promptVersion &&
                    x.ContentHash == contentHash)
                .FirstOrDefaultAsync(ct);

            if (existing == null)
            {
                existing = new AiLegalDocumentSectionSummary
                {
                    CreatedByUserId = createdByUserId,

                    LegalDocumentId = legalDocumentId,
                    TocEntryId = tocId,
                    StartPage = startPage,
                    EndPage = endPage,

                    Type = type,
                    PromptVersion = promptVersion,

                    OwnerKey = ownerKey,
                    ContentHash = contentHash,
                    CacheKey = cacheKey,

                    SectionTitle = sectionTitle,

                    Summary = summary,
                    InputCharCount = inputCharCount,
                    TokensIn = tokensIn,
                    TokensOut = tokensOut,
                    ModelUsed = modelUsed,

                    CreatedAt = DateTime.UtcNow
                };

                _db.AiLegalDocumentSectionSummaries.Add(existing);
            }
            else
            {
                // Keep original CreatedByUserId as "first creator" audit, or update it—your choice.
                // Here we keep it stable and only update content fields.
                existing.Summary = summary;
                existing.SectionTitle = sectionTitle ?? existing.SectionTitle;

                existing.CacheKey = cacheKey; // keep in sync with computation
                existing.InputCharCount = inputCharCount;
                existing.TokensIn = tokensIn;
                existing.TokensOut = tokensOut;
                existing.ModelUsed = modelUsed;

                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
        }
    }
}
