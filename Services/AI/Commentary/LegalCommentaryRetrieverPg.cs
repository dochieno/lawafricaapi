using LawAfrica.API.Data;
using LawAfrica.API.DTOs.AI.Commentary;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace LawAfrica.API.Services.Ai.Commentary
{
    /// <summary>
    /// Postgres FTS retriever for LawReports + LegalDocumentPageTexts.
    /// Uses tsvector.Matches(...) and tsvector.Rank(...) for relevance.
    /// </summary>
    public class LegalCommentaryRetrieverPg : ILegalCommentaryRetriever
    {
        private readonly ApplicationDbContext _db;

        // "english" is a good default for legal text
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

            // Safe query parsing
            var tsQuery = EF.Functions.PlainToTsQuery(TsConfig, q);

            // -----------------------------
            // 1) LAW REPORTS (ranked)
            // -----------------------------
            var lawReportCandidates = await _db.LawReports
                .AsNoTracking()
                .Select(r => new
                {
                    r.Id,
                    Title = r.Parties ?? $"Law Report #{r.Id}",
                    Citation = r.Citation ?? "",
                    Text = r.ContentText,

                    // Build vectors with weights then rank
                    MetaVector =
                        EF.Functions.ToTsVector(
                            TsConfig,
                            (r.Parties ?? "") + " " + (r.Citation ?? "") + " " + (r.Court ?? "") + " " + (r.Judges ?? "")
                        ).SetWeight(NpgsqlTsVector.Lexeme.Weight.A),

                    BodyVector =
                        EF.Functions.ToTsVector(TsConfig, r.ContentText)
                            .SetWeight(NpgsqlTsVector.Lexeme.Weight.B),
                })
                .Where(x => x.MetaVector.Matches(tsQuery) || x.BodyVector.Matches(tsQuery))
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    x.Citation,
                    x.Text,
                    Rank = x.MetaVector.Rank(tsQuery) + x.BodyVector.Rank(tsQuery)
                })
                .OrderByDescending(x => x.Rank)
                .ThenByDescending(x => x.Id)
                .Take(Math.Max(3, maxItems / 2))
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
                    p.Text,
                    Vec = EF.Functions.ToTsVector(TsConfig, p.Text)
                })
                .Where(x => x.Vec.Matches(tsQuery))
                .Select(x => new
                {
                    x.LegalDocumentId,
                    x.PageNumber,
                    x.Text,
                    Rank = x.Vec.Rank(tsQuery)
                })
                .OrderByDescending(x => x.Rank)
                .ThenByDescending(x => x.LegalDocumentId)
                .ThenBy(x => x.PageNumber)
                .Take(Math.Max(6, maxItems * 2)) // we will cap per doc below
                .ToListAsync(ct);

            // -----------------------------
            // 3) Build Sources + Grounding Pack
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

            // Order combined sources by score and cut to maxItems
            var limited = sources
                .OrderByDescending(s => s.Score)
                .Take(maxItems)
                .ToList();

            // Build grounding pack from limited sources
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
