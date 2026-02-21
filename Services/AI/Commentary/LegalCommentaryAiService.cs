// =======================================================
// FILE: LawAfrica.API/Services/Ai/Commentary/LegalCommentaryAiService.cs
// Purpose:
// - AI commentary Q&A with thread persistence
// - Strong LawAfrica grounding + optional external context
// - Correct SPA routes in links (/dashboard/...)
// - Sources footer uses:
//    * LegalDocuments: Title
//    * LawReports: Parties + Citation
// =======================================================

using LawAfrica.API.Data;
using LawAfrica.API.DTOs.AI.Commentary;
using LawAfrica.API.Models.Ai.Commentary;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace LawAfrica.API.Services.Ai.Commentary
{
    public class LegalCommentaryAiService : ILegalCommentaryAiService
    {
        private readonly ApplicationDbContext _db;
        private readonly IAiTextClient _ai;
        private readonly ILegalScopeGuard _scope;
        private readonly ILegalCommentaryRetriever _retriever;
        private readonly IUserJurisdictionResolver _jurisdiction;

        // Used to build absolute/relative links embedded into markdown
        private readonly string _appBaseUrl;

        public LegalCommentaryAiService(
            ApplicationDbContext db,
            IAiTextClient ai,
            ILegalScopeGuard scope,
            ILegalCommentaryRetriever retriever,
            IUserJurisdictionResolver jurisdiction,
            IConfiguration cfg)
        {
            _db = db;
            _ai = ai;
            _scope = scope;
            _retriever = retriever;
            _jurisdiction = jurisdiction;

            _appBaseUrl = (cfg["App:BaseUrl"] ?? "").Trim().TrimEnd('/');
        }

        public async Task<LegalCommentaryAskResponseDto> AskAsync(
            int userId,
            LegalCommentaryAskRequestDto req,
            string userTier,
            CancellationToken ct)
        {
            req ??= new LegalCommentaryAskRequestDto();

            var question = (req.Question ?? "").Trim();
            if (string.IsNullOrWhiteSpace(question))
            {
                return new LegalCommentaryAskResponseDto
                {
                    Declined = true,
                    DeclineReason = "Question is required.",
                    Mode = "basic",
                    Model = _ai.ModelName ?? "",
                    DisclaimerMarkdown = BuildDisclaimer(),
                    ReplyMarkdown = "### Missing question\n- Please type a legal question to continue."
                };
            }

            // 1) Legal-only gate (decline anything non-legal)
            var (ok, reason) = await _scope.IsLegalAsync(question, ct);
            if (!ok)
            {
                return new LegalCommentaryAskResponseDto
                {
                    Declined = true,
                    DeclineReason = reason,
                    Mode = "basic",
                    Model = _ai.ModelName ?? "",
                    DisclaimerMarkdown = BuildDisclaimer(),
                    ReplyMarkdown =
                        "### I can only help with legal questions\n" +
                        "- Please rephrase your question as a **legal issue** (rights, procedure, contract, offence, remedy, compliance, etc.).\n" +
                        "- If you share your **jurisdiction** (e.g., Kenya/Uganda), I can be more precise.\n"
                };
            }

            // 2) Resolve user jurisdiction (DB-based; no guessing)
            var jx = await _jurisdiction.ResolveAsync(userId, ct);

            // 3) Create or load thread (client can start new by sending null ThreadId)
            var thread = await CreateOrLoadThreadAsync(userId, req.ThreadId, jx, req.Mode, ct);

            // 4) Persist the user message
            var normalizedRequestedMode = NormalizeMode(req.Mode, userTier);

            var userMsg = new AiCommentaryMessage
            {
                ThreadId = thread.Id,
                Role = "user",
                ContentMarkdown = question,
                Mode = normalizedRequestedMode,
                Model = null,
                DisclaimerVersion = "v1",
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.AiCommentaryMessages.Add(userMsg);

            // Update title on first message (nice UX)
            if (string.IsNullOrWhiteSpace(thread.Title) || thread.Title == "New conversation")
            {
                thread.Title = MakeThreadTitle(question);
            }

            thread.LastActivityAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // 5) Enforce tier mode (server-side)
            var mode = normalizedRequestedMode;

            // 6) Load last conversation turns from DB (stable history)
            var historyTurns = await LoadHistoryFromDbAsync(thread.Id, takeLast: 8, ct);

            // 7) Retrieve internal sources first (OPTIONAL; never fail the request if retrieval breaks)
            var maxSources = mode == "extended" ? 10 : 5;

            LegalCommentaryRetrievalResult retrieval;
            try
            {
                retrieval = await _retriever.SearchAsync(question, maxSources, ct);
            }
            catch
            {
                retrieval = new LegalCommentaryRetrievalResult
                {
                    Sources = new List<LegalCommentarySourceDto>(),
                    GroundingText = ""
                };
            }

            // ✅ Preload display names for sources so footer shows Titles and Parties/Citation
            var safeSources = retrieval.Sources ?? new List<LegalCommentarySourceDto>();

            var docIds = safeSources
                .Where(s => s.LegalDocumentId.HasValue)
                .Select(s => s.LegalDocumentId!.Value)
                .Distinct()
                .ToList();

            var lrIds = safeSources
                .Where(s => s.LawReportId.HasValue)
                .Select(s => s.LawReportId!.Value)
                .Distinct()
                .ToList();

            var docTitleMap = await LoadDocumentTitlesAsync(docIds, ct);
            var lrDisplayMap = await LoadLawReportDisplayAsync(lrIds, ct);

            // 8) Build system prompt (includes correct /dashboard/... link map)
            var system = BuildSystemPrompt(
                mode: mode,
                allowExternal: req.AllowExternalContext,
                userJurisdiction: jx,
                sources: safeSources,
                docTitleMap: docTitleMap,
                lrDisplayMap: lrDisplayMap
            );

            var prompt = BuildPrompt(
                system,
                retrieval.GroundingText,
                question,
                historyTurns
            );

            // 9) Generate
            var answer = (await _ai.GenerateAsync(prompt, ct) ?? "").Trim();

            if (string.IsNullOrWhiteSpace(answer))
            {
                answer =
                    "### Short answer\n" +
                    "- Not enough information to respond confidently.\n\n" +
                    "### Key issues to clarify\n" +
                    "- Jurisdiction, dates, and the specific legal relationship (contract/employment/crime/etc.).\n";
            }

            // 10) Include sources footer (friendly when none exist) — now uses Titles/Parties+Citation and correct routes
            var sourcesFooter = (safeSources.Count > 0)
                ? BuildSourcesFooter(safeSources, docTitleMap, lrDisplayMap)
                : "### Sources (links)\n- No internal LawAfrica sources matched strongly for this question.";

            var finalMarkdown = (answer + "\n\n" + sourcesFooter).Trim();

            // ✅ NEW: Replace inline Source tokens to show titles (not ids)
            finalMarkdown = RewriteInlineSourceTokens(finalMarkdown, docTitleMap, lrDisplayMap);


            // 11) Persist assistant message + snapshot sources
            var assistantMsg = new AiCommentaryMessage
            {
                ThreadId = thread.Id,
                Role = "assistant",
                ContentMarkdown = finalMarkdown,
                Mode = mode,
                Model = _ai.ModelName ?? "",
                DisclaimerVersion = "v1",
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.AiCommentaryMessages.Add(assistantMsg);
            await _db.SaveChangesAsync(ct); // ensure assistantMsg.Id exists

            if (safeSources.Count > 0)
            {
                foreach (var s in safeSources.OrderByDescending(x => x.Score).Take(maxSources))
                {
                    // snapshot a nicer title into the message source row
                    var snapTitle = (s.Title ?? "").Trim();

                    if (s.Type == "law_report" && s.LawReportId.HasValue)
                    {
                        var id = s.LawReportId.Value;
                        if (lrDisplayMap.TryGetValue(id, out var disp))
                        {
                            snapTitle = NiceLawReportTitle(id, disp);
                        }
                    }
                    else if ((s.Type == "pdf_page" || s.Type == "document" || s.Type == "legal_document") && s.LegalDocumentId.HasValue)
                    {
                        var did = s.LegalDocumentId.Value;
                        if (docTitleMap.TryGetValue(did, out var dt) && !string.IsNullOrWhiteSpace(dt))
                        {
                            snapTitle = dt;
                        }
                    }

                    _db.AiCommentaryMessageSources.Add(new AiCommentaryMessageSource
                    {
                        MessageId = assistantMsg.Id,
                        Type = (s.Type ?? "").Trim(),
                        Score = s.Score,
                        Title = snapTitle,
                        Citation = s.Citation,
                        Snippet = s.Snippet,
                        LawReportId = s.LawReportId,
                        LegalDocumentId = s.LegalDocumentId,
                        PageNumber = s.PageNumber,
                        LinkUrl = DeriveLinkUrl(s) // MUST return /dashboard/... routes
                    });
                }
            }

            thread.LastActivityAtUtc = DateTime.UtcNow;
            thread.LastModel = _ai.ModelName ?? "";
            await _db.SaveChangesAsync(ct);

            return new LegalCommentaryAskResponseDto
            {
                Declined = false,
                Mode = mode,
                Model = _ai.ModelName ?? "",
                DisclaimerMarkdown = BuildDisclaimer(),
                ReplyMarkdown = finalMarkdown,
                Sources = safeSources,

                // ✅ IMPORTANT: frontend uses this for continuity
                ThreadId = thread.Id
            };
        }

        // ------------------------------------------------------------
        // Thread + history persistence helpers
        // ------------------------------------------------------------

        private async Task<AiCommentaryThread> CreateOrLoadThreadAsync(
            int userId,
            long? threadId,
            UserJurisdictionContext jx,
            string? requestedMode,
            CancellationToken ct)
        {
            if (threadId.HasValue)
            {
                var existing = await _db.AiCommentaryThreads
                    .FirstOrDefaultAsync(x => x.Id == threadId.Value && x.UserId == userId && !x.IsDeleted, ct);

                if (existing == null)
                    throw new InvalidOperationException("Thread not found (or you do not have access).");

                return existing;
            }

            var mode = (requestedMode ?? "").Trim().ToLowerInvariant();
            mode = mode == "extended" ? "extended" : "basic";

            var t = new AiCommentaryThread
            {
                UserId = userId,
                Title = "New conversation",
                Mode = mode,
                CountryName = jx.CountryName,
                CountryIso = jx.CountryIso,
                RegionLabel = jx.RegionLabel,
                CreatedAtUtc = DateTime.UtcNow,
                LastActivityAtUtc = DateTime.UtcNow,
                IsDeleted = false
            };

            _db.AiCommentaryThreads.Add(t);
            await _db.SaveChangesAsync(ct);

            return t;
        }

        private async Task<List<LegalCommentaryTurnDto>> LoadHistoryFromDbAsync(long threadId, int takeLast, CancellationToken ct)
        {
            var msgs = await _db.AiCommentaryMessages
                .AsNoTracking()
                .Where(x => x.ThreadId == threadId && !x.IsDeleted)
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => new { x.Role, x.ContentMarkdown })
                .ToListAsync(ct);

            return msgs
                .TakeLast(Math.Max(0, takeLast))
                .Select(x => new LegalCommentaryTurnDto
                {
                    Role = (x.Role ?? "user").Trim().ToLowerInvariant(),
                    Content = (x.ContentMarkdown ?? "").Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Content))
                .ToList();
        }

        private static string MakeThreadTitle(string question)
        {
            var s = (question ?? "").Trim();
            if (s.Length <= 80) return s;
            return s.Substring(0, 80).Trim() + "…";
        }

        // ------------------------------------------------------------
        // Prompt + formatting helpers
        // ------------------------------------------------------------

        private static string NormalizeMode(string? requested, string userTier)
        {
            var r = (requested ?? "").Trim().ToLowerInvariant();
            var wantsExtended = r == "extended";

            if (!string.Equals(userTier, "extended", StringComparison.OrdinalIgnoreCase))
                return "basic";

            return wantsExtended ? "extended" : "basic";
        }

        private string BuildSystemPrompt(
            string mode,
            bool allowExternal,
            UserJurisdictionContext userJurisdiction,
            List<LegalCommentarySourceDto> sources,
            Dictionary<int, string> docTitleMap,
            Dictionary<int, (string Parties, string Citation)> lrDisplayMap)
        {
            var primary = string.IsNullOrWhiteSpace(userJurisdiction.CountryName)
                ? "Unknown"
                : userJurisdiction.CountryName.Trim();

            var region = string.IsNullOrWhiteSpace(userJurisdiction.RegionLabel)
                ? "Africa"
                : userJurisdiction.RegionLabel.Trim();

            var who = string.IsNullOrWhiteSpace(userJurisdiction.DisplayName)
                ? "the user"
                : userJurisdiction.DisplayName.Trim();

            var city = string.IsNullOrWhiteSpace(userJurisdiction.City)
                ? "Unknown"
                : userJurisdiction.City.Trim();

            // Provide the model a canonical link map it can reuse in markdown
            var linkMap = BuildLinkMapForPrompt(sources, docTitleMap, lrDisplayMap);

            // Strict toggle text for external context
            var externalRule = allowExternal
                ? "You MAY add general legal context not contained in LawAfrica sources, BUT you must still follow the jurisdiction rules below."
                : "You MUST NOT add external context. Use only LawAfrica sources + general legal reasoning without linking to outside sources.";

            return $@"
                You are Legal Commentary AI for LawAfrica.

                USER CONTEXT:
                - Name: {who}
                - City: {city}
                - Primary jurisdiction: {primary}
                - Region: {region}

                MISSION:
                - Answer the user's legal question with a DB-grounded response first.
                - You are NOT tied to one document; you may combine relevant LawAfrica sources.

                HARD RULES (NON-NEGOTIABLE):
                - Answer ONLY legal questions. If non-legal, refuse.
                - Prefer LawAfrica internal sources first (provided in the SOURCES PACK).
                - Do NOT invent case citations, page numbers, quotations, holdings, or statutes.
                - When you rely on a LawAfrica excerpt, cite it inline using these tokens exactly:
                  (Source: LAW_REPORT:ID) or (Source: PDF_PAGE:DOC=ID:PAGE=N)
                - If the SOURCES PACK does not support a claim, say: ""Not confirmed in LawAfrica sources."" (do not guess).

                IF NO INTERNAL SOURCES:
                - The SOURCES PACK may be empty. You MUST still answer using general legal knowledge.
                - HOWEVER, you must still follow the jurisdiction rules below.
                - Do NOT invent citations, quotes, or page numbers.

                JURISDICTION RULES (STRICT — ENFORCE THIS ORDER):
                - You MUST start with **{primary}** law and practice FIRST.
                - You MUST NOT lead with UK/EU/Finland/US examples.
                - If {primary} is ""Unknown"": 
                  - Ask for the jurisdiction as a ""Key issue to clarify"" 
                  - Keep the answer high-level and general (no country-specific claims).
                - If you mention ANY non-{primary} jurisdiction:
                  - Put it ONLY under: ""### Comparative note (other jurisdictions)""
                  - Label the jurisdiction clearly (e.g., ""UK"", ""Finland"", ""EU"") 
                  - Keep it short and clearly secondary.

                SPECIAL RULE FOR EMPLOYMENT / TERMINATION QUESTIONS:
                - If the question is employment-related and {primary} is Kenya:
                  - Begin with Kenya employment law context first (unfair termination principles, procedure, remedies).
                  - Do not cite specific sections unless supported by SOURCES PACK; otherwise keep statutory references general.

                LINKING RULES (IMPORTANT):
                - Whenever you mention a LawAfrica internal source (case/document/page), include a clickable markdown link using the LINK MAP below.
                  Examples:
                  - ... (Source: LAW_REPORT:123) — [Open source](/dashboard/law-reports/123)
                  - ... (Source: PDF_PAGE:DOC=55:PAGE=12) — [Open](/dashboard/documents/55?page=12)

                - Whenever you mention an Act by name (e.g., ""Employment Act"", ""Finance Act"", ""Companies Act""):
                  - Provide a link where the user can find it using internal search:
                    [Find: Employment Act](/dashboard/search?kind=acts&q=Employment%20Act)

                EXTERNAL CONTEXT:
                - {externalRule}

                - If external context is allowed and you add it:
                  - You MUST add a section at the end: ""### External sources (links)""
                  - Provide 2–5 REAL clickable links (full https URLs) to reputable sources, ideally for {primary} or {region}.
                  - Do NOT invent links or citations.

                RESPONSE STRUCTURE (MANDATORY — DO NOT CHANGE ORDER):
                ### Short answer ({primary})
                ### Key issues to clarify ({primary})
                ### {primary} legal position
                ### Practical next steps in {primary}
                ### Risks / deadlines / cautions in {primary}
                ### Comparative note (other jurisdictions) (optional; only if helpful)
                ### Sources (links)
                {(allowExternal ? "### External sources (links) (only if you actually used external sources)" : "")}

                STYLE:
                - Use bullets (-). Avoid long paragraphs.
                - Use **bold** for key terms only.
                - Do NOT use numbered lists unless the user asks.
                - Be precise, cautious, and practical.

                Mode: {mode}

                LINK MAP (use these exact links):
                {linkMap}
                ".Trim();
        }

        private static string BuildPrompt(
            string system,
            string sourcesPack,
            string question,
            List<LegalCommentaryTurnDto>? history)
        {
            var lines = new List<string> { system.Trim() };

            var turns = (history ?? new List<LegalCommentaryTurnDto>()).TakeLast(8);
            foreach (var t in turns)
            {
                var role = (t.Role ?? "user").Trim().ToLowerInvariant();
                if (role != "user" && role != "assistant") role = "user";

                var content = (t.Content ?? "").Trim();
                if (string.IsNullOrWhiteSpace(content)) continue;

                lines.Add($"{role.ToUpperInvariant()}: {content}");
            }

            lines.Add("");
            lines.Add("SOURCES PACK (LawAfrica internal):");
            lines.Add(string.IsNullOrWhiteSpace(sourcesPack) ? "(No strong internal matches found.)" : sourcesPack);
            lines.Add("");
            lines.Add("QUESTION:");
            lines.Add(question);

            return string.Join("\n", lines);
        }

        private string BuildLinkMapForPrompt(
            List<LegalCommentarySourceDto> sources,
            Dictionary<int, string> docTitleMap,
            Dictionary<int, (string Parties, string Citation)> lrDisplayMap)
        {
            if (sources == null || sources.Count == 0)
            {
                return "- (no internal sources matched strongly)\n" +
                       "- ACT_SEARCH(pattern) => " + MakeUrl("/dashboard/search?kind=acts&q=<Act%20Name%20Here>");
            }

            var lines = new List<string>();

            foreach (var s in sources)
            {
                if (s.Type == "law_report" && s.LawReportId.HasValue)
                {
                    var url = MakeUrl($"/dashboard/law-reports/{s.LawReportId.Value}");
                    lines.Add($"- LAW_REPORT:{s.LawReportId.Value} => {url}");
                }
                else if (s.Type == "pdf_page" && s.LegalDocumentId.HasValue && s.PageNumber.HasValue)
                {
                    // Details page (your preference), keep page hint
                    var url = MakeUrl($"/dashboard/documents/{s.LegalDocumentId.Value}?page={s.PageNumber.Value}");
                    lines.Add($"- PDF_PAGE:DOC={s.LegalDocumentId.Value}:PAGE={s.PageNumber.Value} => {url}");
                }
                else if (s.LegalDocumentId.HasValue)
                {
                    var url = MakeUrl($"/dashboard/documents/{s.LegalDocumentId.Value}");
                    lines.Add($"- DOC:{s.LegalDocumentId.Value} => {url}");
                }
            }

            lines.Add($"- ACT_SEARCH(pattern) => {MakeUrl("/dashboard/search?kind=acts&q=<Act%20Name%20Here>")}");
            return string.Join("\n", lines);
        }

        private string BuildSourcesFooter(
            List<LegalCommentarySourceDto> sources,
            Dictionary<int, string> docTitleMap,
            Dictionary<int, (string Parties, string Citation)> lrDisplayMap)
        {
            if (sources == null || sources.Count == 0)
                return "### Sources (links)\n- No internal LawAfrica sources matched strongly for this question.";

            var lines = new List<string> { "### Sources (links)" };

            foreach (var s in sources.OrderByDescending(x => x.Score))
            {
                if (s.Type == "law_report" && s.LawReportId.HasValue)
                {
                    var id = s.LawReportId.Value;
                    var url = MakeUrl($"/dashboard/law-reports/{id}");

                    var display = lrDisplayMap.TryGetValue(id, out var d)
                        ? NiceLawReportTitle(id, d)
                        : (string.IsNullOrWhiteSpace(s.Title) ? $"Law Report #{id}" : s.Title.Trim());

                    lines.Add($"- **{EscapeMd(display)}** — [Open]({url})");
                }
                else if (s.Type == "pdf_page" && s.LegalDocumentId.HasValue && s.PageNumber.HasValue)
                {
                    var did = s.LegalDocumentId.Value;
                    var page = s.PageNumber.Value;

                    var url = MakeUrl($"/dashboard/documents/{did}?page={page}");

                    var title = docTitleMap.TryGetValue(did, out var t) && !string.IsNullOrWhiteSpace(t)
                        ? t
                        : (string.IsNullOrWhiteSpace(s.Title) ? $"Document #{did}" : s.Title.Trim());

                    lines.Add($"- **{EscapeMd(title)}** — Page {page} — [Open]({url})");
                }
                else if (s.LegalDocumentId.HasValue)
                {
                    var did = s.LegalDocumentId.Value;
                    var url = MakeUrl($"/dashboard/documents/{did}");

                    var title = docTitleMap.TryGetValue(did, out var t) && !string.IsNullOrWhiteSpace(t)
                        ? t
                        : (string.IsNullOrWhiteSpace(s.Title) ? $"Document #{did}" : s.Title.Trim());

                    lines.Add($"- **{EscapeMd(title)}** — [Open]({url})");
                }
            }

            lines.Add($"- **Acts search** — [Find an Act]({MakeUrl("/dashboard/search?kind=acts&q=Employment%20Act")})");
            return string.Join("\n", lines);
        }

        private string DeriveLinkUrl(LegalCommentarySourceDto s)
        {
            if (s.Type == "law_report" && s.LawReportId.HasValue)
                return MakeUrl($"/dashboard/law-reports/{s.LawReportId.Value}");

            if (s.Type == "pdf_page" && s.LegalDocumentId.HasValue && s.PageNumber.HasValue)
                return MakeUrl($"/dashboard/documents/{s.LegalDocumentId.Value}?page={s.PageNumber.Value}");

            if (s.LegalDocumentId.HasValue)
                return MakeUrl($"/dashboard/documents/{s.LegalDocumentId.Value}");

            return "";
        }

        private string MakeUrl(string relativeOrAbsolute)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolute)) return relativeOrAbsolute;

            if (relativeOrAbsolute.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                relativeOrAbsolute.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return relativeOrAbsolute;

            if (string.IsNullOrWhiteSpace(_appBaseUrl))
                return relativeOrAbsolute;

            if (!relativeOrAbsolute.StartsWith("/")) relativeOrAbsolute = "/" + relativeOrAbsolute;
            return _appBaseUrl + relativeOrAbsolute;
        }

        private static string EscapeMd(string s)
        {
            return (s ?? "")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace("(", "\\(")
                .Replace(")", "\\)")
                .Replace("|", "\\|");
        }

        private static string BuildDisclaimer()
        {
            return
@"> **Disclaimer (LawAfrica LegalAI):** This is AI-generated legal information for general guidance, not legal advice.  
> It may be incomplete or incorrect and does not create an advocate–client relationship.  
> Consult a qualified advocate for advice on your facts, deadlines, filings, or court strategy.";
        }

        // ------------------------------------------------------------
        // Display helpers for nicer Sources footer labels
        // ------------------------------------------------------------

        private async Task<Dictionary<int, string>> LoadDocumentTitlesAsync(IEnumerable<int> docIds, CancellationToken ct)
        {
            var ids = docIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, string>();

            // NOTE: assumes LegalDocuments has (Id, Title)
            return await _db.LegalDocuments
                .AsNoTracking()
                .Where(d => ids.Contains(d.Id))
                .Select(d => new { d.Id, d.Title })
                .ToDictionaryAsync(x => x.Id, x => (x.Title ?? "").Trim(), ct);
        }

        private async Task<Dictionary<int, (string Parties, string Citation)>> LoadLawReportDisplayAsync(IEnumerable<int> lrIds, CancellationToken ct)
        {
            var ids = lrIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, (string, string)>();

            // NOTE: assumes LawReports has (Id, Parties, Citation)
            return await _db.LawReports
                .AsNoTracking()
                .Where(r => ids.Contains(r.Id))
                .Select(r => new { r.Id, r.Parties, r.Citation })
                .ToDictionaryAsync(
                    x => x.Id,
                    x => ((x.Parties ?? "").Trim(), (x.Citation ?? "").Trim()),
                    ct
                );
        }

        public record AskStreamChunk(string? DeltaText, long? ThreadId, bool Done);

        public async IAsyncEnumerable<AskStreamChunk> AskStreamAsync(
            int userId,
            LegalCommentaryAskRequestDto req,
            string tier,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var resp = await AskAsync(userId, req, tier, ct);
            var full = (resp?.ReplyMarkdown ?? "").Trim();
            var threadId = resp?.ThreadId;

            const int size = 40;

            for (int i = 0; i < full.Length; i += size)
            {
                var part = full.Substring(i, Math.Min(size, full.Length - i));
                yield return new AskStreamChunk(part, threadId, Done: false);
                await Task.Delay(10, ct);
            }

            yield return new AskStreamChunk(null, threadId, Done: true);
        }

        // ...

        private string RewriteInlineSourceTokens(
    string markdown,
    Dictionary<int, string> docTitleMap,
    Dictionary<int, (string Parties, string Citation)> lrDisplayMap)
    {
        var s = markdown ?? "";
        if (string.IsNullOrWhiteSpace(s)) return s;

        // LAW_REPORT:123
        s = Regex.Replace(
            s,
            @"\bLAW_REPORT:(\d+)\b",
            m =>
            {
                if (!int.TryParse(m.Groups[1].Value, out var id)) return m.Value;

                if (lrDisplayMap != null && lrDisplayMap.TryGetValue(id, out var disp))
                    return EscapeMd(NiceLawReportTitle(id, disp));

                return $"Law Report #{id}";
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        // PDF_PAGE:DOC=74:PAGE=463
        s = Regex.Replace(
            s,
            @"\bPDF_PAGE:DOC=(\d+):PAGE=(\d+)\b",
            m =>
            {
                if (!int.TryParse(m.Groups[1].Value, out var did)) return m.Value;
                if (!int.TryParse(m.Groups[2].Value, out var page)) return m.Value;

                var title = (docTitleMap != null && docTitleMap.TryGetValue(did, out var t) && !string.IsNullOrWhiteSpace(t))
                    ? EscapeMd(t)
                    : $"Document #{did}";

                return $"{title}, p. {page}";
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        // DOC:74 (if your model ever uses it)
        s = Regex.Replace(
            s,
            @"\bDOC:(\d+)\b",
            m =>
            {
                if (!int.TryParse(m.Groups[1].Value, out var did)) return m.Value;

                var title = (docTitleMap != null && docTitleMap.TryGetValue(did, out var t) && !string.IsNullOrWhiteSpace(t))
                    ? EscapeMd(t)
                    : $"Document #{did}";

                return title;
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        return s;
    }


    private static string NiceLawReportTitle(int id, (string Parties, string Citation) d)
        {
            var parties = (d.Parties ?? "").Trim();
            var cite = (d.Citation ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(parties) && !string.IsNullOrWhiteSpace(cite))
                return $"{parties} — {cite}";
            if (!string.IsNullOrWhiteSpace(parties))
                return parties;
            if (!string.IsNullOrWhiteSpace(cite))
                return cite;

            return $"Law Report #{id}";
        }
    }
}
