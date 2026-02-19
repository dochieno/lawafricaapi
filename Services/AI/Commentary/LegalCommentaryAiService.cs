using LawAfrica.API.Data;
using LawAfrica.API.DTOs.AI.Commentary;
using LawAfrica.API.Models.Ai.Commentary;
using Microsoft.EntityFrameworkCore;

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
            var userMsg = new AiCommentaryMessage
            {
                ThreadId = thread.Id,
                Role = "user",
                ContentMarkdown = question,
                Mode = NormalizeMode(req.Mode, userTier),
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
            var mode = NormalizeMode(req.Mode, userTier);

            // 6) Load last conversation turns from DB (stable history)
            var historyTurns = await LoadHistoryFromDbAsync(thread.Id, takeLast: 8, ct);

            // 7) Retrieve internal sources first
            var maxSources = mode == "extended" ? 10 : 5;
            // 7) Retrieve internal sources first (OPTIONAL; never fail the request if retrieval breaks)
            LegalCommentaryRetrievalResult retrieval;
            try
            {
                retrieval = await _retriever.SearchAsync(question, maxSources, ct);
            }
            catch (Exception ex)
            {
                // IMPORTANT: retrieval must never break the endpoint
                // If you have ILogger injected, log here. Otherwise keep it silent.
                retrieval = new LegalCommentaryRetrievalResult
                {
                    Sources = new List<LegalCommentarySourceDto>(),
                    GroundingText = ""
                };
            }


            // 8) Build system prompt
            var system = BuildSystemPrompt(
                mode: mode,
                allowExternal: req.AllowExternalContext,
                userJurisdiction: jx,
                sources: retrieval.Sources
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

            // 10) Always include sources footer
            // 10) Include sources footer (friendly when none exist)
            var sourcesFooter = (retrieval.Sources != null && retrieval.Sources.Count > 0)
                ? BuildSourcesFooter(retrieval.Sources)
                : "### Sources (links)\n- No internal LawAfrica sources matched strongly for this question.";

            var finalMarkdown = (answer + "\n\n" + sourcesFooter).Trim();


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

            if (retrieval.Sources != null && retrieval.Sources.Count > 0)
            {
                foreach (var s in retrieval.Sources.OrderByDescending(x => x.Score).Take(maxSources))
                {
                    _db.AiCommentaryMessageSources.Add(new AiCommentaryMessageSource
                    {
                        MessageId = assistantMsg.Id,
                        Type = (s.Type ?? "").Trim(),
                        Score = s.Score,
                        Title = s.Title,
                        Citation = s.Citation,
                        Snippet = s.Snippet,
                        LawReportId = s.LawReportId,
                        LegalDocumentId = s.LegalDocumentId,
                        PageNumber = s.PageNumber,
                        LinkUrl = DeriveLinkUrl(s)
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
                Sources = retrieval.Sources ?? new List<LegalCommentarySourceDto>(),

                // ✅ IMPORTANT:
                // Your response DTO should include ThreadId so frontend can keep sending it.
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
        // Prompt + formatting helpers (mostly your existing code)
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
            List<LegalCommentarySourceDto> sources)
        {
            var primary = string.IsNullOrWhiteSpace(userJurisdiction.CountryName) ? "Unknown" : userJurisdiction.CountryName.Trim();
            var region = string.IsNullOrWhiteSpace(userJurisdiction.RegionLabel) ? "Region" : userJurisdiction.RegionLabel.Trim();

            // Provide the model a canonical link map it can reuse in markdown
            var linkMap = BuildLinkMapForPrompt(sources);

            return $@"
                You are Legal Commentary AI for LawAfrica.

                MISSION:
                - Answer the user's legal question with a DB-grounded response first.
                - You are NOT tied to one document; you may combine relevant LawAfrica sources.

                HARD RULES:
                - Answer ONLY legal questions. If non-legal, refuse.
                - Prefer LawAfrica internal sources first (provided in the SOURCES PACK).
                - Do NOT invent case citations, page numbers, quotations, holdings, or statutes.
                - When you rely on a LawAfrica excerpt, cite it inline:
                  (Source: LAW_REPORT:ID) or (Source: PDF_PAGE:DOC=ID:PAGE=N)
                - If the SOURCES PACK does not support a claim, say: ""Not confirmed in LawAfrica sources.""
                IF NO INTERNAL SOURCES:
                - The SOURCES PACK may be empty. In that case, you MUST still answer using general legal knowledge.
                - Put that content under: ""General legal context (not from LawAfrica sources)"".
                - Do NOT invent citations, quotes, or page numbers.

                JURISDICTION BIAS (MANDATORY for any content NOT supported by SOURCES PACK):
                - Primary jurisdiction: **{primary}**
                - Then: **{region}**
                - Then: **Africa**
                - Then: general/common-law/global principles as last resort
                - If you mention rules from outside {primary}, clearly label the jurisdiction.
                - If the answer depends on jurisdiction-specific statutes/procedure and SOURCES PACK does not confirm them,
                  list the missing details under ""Key issues to clarify"" and keep guidance high-level.

                LINKING RULES (IMPORTANT):
                - Whenever you mention a LawAfrica internal source (case/document/page), include a clickable markdown link using the LINK MAP below.
                  Example:
                  - ... (Source: LAW_REPORT:123) — [Open source](/law-reports/123)
                  - ... (Source: PDF_PAGE:DOC=55:PAGE=12) — [Open page](/documents/55?page=12)

                - Whenever you mention an Act by name (e.g., ""Employment Act"", ""Finance Act"", ""Companies Act""):
                  - Provide a link where the user can find it.
                  - Use the internal search link format:
                    [Find: Employment Act](/search?kind=acts&q=Employment%20Act)
                  - If jurisdiction/year is known, include it:
                    [Find: Finance Act 2023 {primary}](/search?kind=acts&q=Finance%20Act%202023%20{Uri.EscapeDataString(primary)})

                EXTERNAL CONTEXT:
                - {(allowExternal ? "You MAY add general legal context not contained in LawAfrica sources." : "You MUST NOT add external context.")}
                - If you add external context, label the section clearly as:
                  ""General legal context (not from LawAfrica sources)"".

                FORMATTING (beautiful markdown):
                - Start with the standard sections below.
                - Inside ""What LawAfrica sources say"" (and external context if allowed), group the analysis by TOPICS.
                - Use clear topic headings in this format:
                  ## Topic: Unfair termination
                  ## Topic: Notice & severance
                  ## Topic: Procedure & timelines
                - Keep each topic concise and bullet-led.

                ### Short answer
                ### Key issues to clarify
                ### What LawAfrica sources say
                ### General legal context (not from LawAfrica sources)  (only if allowed)
                ### Practical next steps
                ### Risks / deadlines / cautions
                ### Sources (links)

                STYLE:
                - Use bullets (-). Avoid long paragraphs.
                - Use **bold** for key terms.
                - Do NOT use numbered lists unless the user asks.
                - Be precise and cautious; avoid overconfident statements.

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

        private string BuildLinkMapForPrompt(List<LegalCommentarySourceDto> sources)
        {
            if (sources == null || sources.Count == 0)
                return "- (no internal sources matched strongly)\n- ACT_SEARCH(pattern) => " + MakeUrl("/search?kind=acts&q=<Act%20Name%20Here>");

            var lines = new List<string>();

            foreach (var s in sources)
            {
                if (s.Type == "law_report" && s.LawReportId.HasValue)
                {
                    var url = MakeUrl($"/law-reports/{s.LawReportId.Value}");
                    lines.Add($"- LAW_REPORT:{s.LawReportId.Value} => {url}");
                }
                else if (s.Type == "pdf_page" && s.LegalDocumentId.HasValue && s.PageNumber.HasValue)
                {
                    var url = MakeUrl($"/documents/{s.LegalDocumentId.Value}?page={s.PageNumber.Value}");
                    lines.Add($"- PDF_PAGE:DOC={s.LegalDocumentId.Value}:PAGE={s.PageNumber.Value} => {url}");
                }
                else if (s.LegalDocumentId.HasValue)
                {
                    var url = MakeUrl($"/documents/{s.LegalDocumentId.Value}");
                    lines.Add($"- DOC:{s.LegalDocumentId.Value} => {url}");
                }
            }

            lines.Add($"- ACT_SEARCH(pattern) => {MakeUrl("/search?kind=acts&q=<Act%20Name%20Here>")}");

            return string.Join("\n", lines);
        }

        private string BuildSourcesFooter(List<LegalCommentarySourceDto> sources)
        {
            if (sources == null || sources.Count == 0)
                return "### Sources (links)\n- No internal LawAfrica sources matched strongly for this question.";

            var lines = new List<string> { "### Sources (links)" };

            foreach (var s in sources.OrderByDescending(x => x.Score))
            {
                if (s.Type == "law_report" && s.LawReportId.HasValue)
                {
                    var url = MakeUrl($"/law-reports/{s.LawReportId.Value}");
                    var title = string.IsNullOrWhiteSpace(s.Title) ? $"Law Report #{s.LawReportId.Value}" : s.Title.Trim();
                    var cite = string.IsNullOrWhiteSpace(s.Citation) ? "" : $" — *{s.Citation.Trim()}*";
                    lines.Add($"- **{EscapeMd(title)}**{cite} — [Open]({url})");
                }
                else if (s.Type == "pdf_page" && s.LegalDocumentId.HasValue && s.PageNumber.HasValue)
                {
                    var url = MakeUrl($"/documents/{s.LegalDocumentId.Value}?page={s.PageNumber.Value}");
                    lines.Add($"- **LegalDocument #{s.LegalDocumentId.Value}** — Page {s.PageNumber.Value} — [Open page]({url})");
                }
                else if (s.LegalDocumentId.HasValue)
                {
                    var url = MakeUrl($"/documents/{s.LegalDocumentId.Value}");
                    lines.Add($"- **LegalDocument #{s.LegalDocumentId.Value}** — [Open]({url})");
                }
            }

            lines.Add($"- **Acts search** — [Find an Act]({MakeUrl("/search?kind=acts&q=Employment%20Act")})");

            return string.Join("\n", lines);
        }

        private string DeriveLinkUrl(LegalCommentarySourceDto s)
        {
            if (s.Type == "law_report" && s.LawReportId.HasValue)
                return MakeUrl($"/law-reports/{s.LawReportId.Value}");

            if (s.Type == "pdf_page" && s.LegalDocumentId.HasValue && s.PageNumber.HasValue)
                return MakeUrl($"/documents/{s.LegalDocumentId.Value}?page={s.PageNumber.Value}");

            if (s.LegalDocumentId.HasValue)
                return MakeUrl($"/documents/{s.LegalDocumentId.Value}");

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
    }
}
