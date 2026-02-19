using LawAfrica.API.Data;
using LawAfrica.API.DTOs.AI.Commentary;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Ai.Commentary
{
    /// <summary>
    /// Postgres FTS retriever for LawReports + LegalDocumentPageTexts.
    /// Uses to_tsvector + plainto_tsquery + rank for relevance.
    ///
    /// IMPORTANT:
    /// - Keep the query fully server-translatable (avoid SetWeight chains that can trigger client eval).
    /// </summary>
    public class LegalCommentaryRetrieverPg : ILegalCommentaryRetriever
    {
        private readonly ApplicationDbContext _db;

        private const string TsConfig = "english";

        public LegalCommentaryRetrieverPg(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<LegalCommentaryRetrievalResult> SearchAsync(
            string question,
            int maxItems,
            CancellationToken ct)
        {
            var q = (question ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q))
                return new LegalCommentaryRetrievalResult();

            // ✅ Must stay server-side (do NOT trigger client eval in the query)
            var tsQuery = EF.Functions.PlainToTsQuery(TsConfig, q);

            // -----------------------------
            // 1) LAW REPORTS (ranked)
            // -----------------------------
            // ✅ Avoid SetWeight(...) because it can cause translation fallback depending on provider/version.
            // Instead: create two texts (meta + body) and rank them separately.
            var lawReportCandidates = await _db.LawReports
                .AsNoTracking()
                .Select(r => new
                {
                    r.Id,
                    Title = r.Parties ?? $"Law Report #{r.Id}",
                    Citation = r.Citation ?? "",
                    BodyText = r.ContentText ?? "",
                    MetaText =
                        (r.Parties ?? "") + " " +
                        (r.Citation ?? "") + " " +
                        (r.Court ?? "") + " " +
                        (r.Judges ?? "")
                })
                .Where(x =>
                    EF.Functions.ToTsVector(TsConfig, x.MetaText).Matches(tsQuery) ||
                    EF.Functions.ToTsVector(TsConfig, x.BodyText).Matches(tsQuery))
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    x.Citation,
                    Text = x.BodyText,
                    Rank =
                        EF.Functions.ToTsVector(TsConfig, x.MetaText).Rank(tsQuery) +
                        EF.Functions.ToTsVector(TsConfig, x.BodyText).Rank(tsQuery)
                })
                .OrderByDescending(x => x.Rank)
                .ThenByDescending(x => x.Id)
                .Take(Math.Max(3, Math.Max(1, maxItems / 2)))
                .ToListAsync(ct);

            // -----------------------------
            // 2) PDF PAGE TEXTS (ranked)
            // -----------------------------
            var pageCandidates = await _db.LegalDocumentPageTexts
                .AsNoTracking()
                .Select(p => new
                {
                    p.LegalDocumentId,
                    p.PageNumber,
                    Text = p.Text ?? ""
                })
                .Where(x => EF.Functions.ToTsVector(TsConfig, x.Text).Matches(tsQuery))
                .Select(x => new
                {
                    x.LegalDocumentId,
                    x.PageNumber,
                    x.Text,
                    Rank = EF.Functions.ToTsVector(TsConfig, x.Text).Rank(tsQuery)
                })
                .OrderByDescending(x => x.Rank)
                .ThenByDescending(x => x.LegalDocumentId)
                .ThenBy(x => x.PageNumber)
                .Take(Math.Max(6, maxItems * 2))
                .ToListAsync(ct);

            // -----------------------------
            // 3) Build sources + grounding pack
            // -----------------------------
            var sources = new List<LegalCommentarySourceDto>();

            foreach (var r in lawReportCandidates)
            {
                sources.Add(new LegalCommentarySourceDto
                {
                    Type = "law_report",
                    LawReportId = r.Id,
                    Title = r.Title,
                    Citation = r.Citation,
                    Snippet = MakeSnippet(r.Text, q),
                    Score = (double)r.Rank
                });
            }

            // Deduplicate PDF pages: max 2 pages per LegalDocument
            var perDocCount = new Dictionary<int, int>();

            foreach (var p in pageCandidates)
            {
                perDocCount.TryGetValue(p.LegalDocumentId, out var c);
                if (c >= 2) continue;
                perDocCount[p.LegalDocumentId] = c + 1;

                sources.Add(new LegalCommentarySourceDto
                {
                    Type = "pdf_page",
                    LegalDocumentId = p.LegalDocumentId,
                    PageNumber = p.PageNumber,
                    Title = $"LegalDocument #{p.LegalDocumentId}",
                    Citation = $"Page {p.PageNumber}",
                    Snippet = MakeSnippet(p.Text, q),
                    Score = (double)p.Rank
                });
            }

            // Limit sources to maxItems
            var limited = sources
                .OrderByDescending(s => s.Score)
                .Take(Math.Max(1, maxItems))
                .ToList();

            // Build grounding pack
            var grounding = new List<string>();
            foreach (var s in limited)
            {
                if (s.Type == "law_report")
                {
                    grounding.Add($"[LAW_REPORT:{s.LawReportId}] {s.Title} {(string.IsNullOrWhiteSpace(s.Citation) ? "" : $"({s.Citation})")}".Trim());
                    grounding.Add($"EXCERPT: \"{s.Snippet}\"");
                    grounding.Add("");
                }
                else
                {
                    grounding.Add($"[PDF_PAGE:DOC={s.LegalDocumentId}:PAGE={s.PageNumber}]");
                    grounding.Add($"EXCERPT: \"{s.Snippet}\"");
                    grounding.Add("");
                }
            }

            return new LegalCommentaryRetrievalResult
            {
                Sources = limited,
                GroundingText = string.Join("\n", grounding).Trim()
            };
        }

        private static string MakeSnippet(string text, string question)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var t = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length <= 520) return t;

            var tokens = question.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                 .Select(x => x.Trim().ToLowerInvariant())
                                 .Where(x => x.Length >= 4)
                                 .Take(8)
                                 .ToArray();

            var lower = t.ToLowerInvariant();
            var idx = -1;
            foreach (var tok in tokens)
            {
                idx = lower.IndexOf(tok, StringComparison.Ordinal);
                if (idx >= 0) break;
            }

            if (idx < 0) return t.Substring(0, 520).Trim() + "…";

            var start = Math.Max(0, idx - 150);
            var len = Math.Min(520, t.Length - start);
            var window = t.Substring(start, len).Trim();

            if (start > 0) window = "…" + window;
            if (start + len < t.Length) window = window + "…";
            return window;
        }
    }
}
