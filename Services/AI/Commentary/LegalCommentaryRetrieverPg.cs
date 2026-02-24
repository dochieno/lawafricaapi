using LawAfrica.API.Data;
using LawAfrica.API.DTOs.AI.Commentary;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LawAfrica.API.Services.Ai.Commentary
{
    /// <summary>
    /// Postgres FTS retriever for LawReports + LegalDocumentPageTexts.
    ///
    /// Improvements vs previous version:
    /// ✅ Uses websearch_to_tsquery for better question-style matching
    /// ✅ Fixes a bug where PDF page search mistakenly used lawLimit instead of pageLimit
    /// ✅ Joins LegalDocuments to return real document titles for pdf pages
    /// ✅ Adds fallback search when FTS returns zero (handles OCR "glued words" like 2016Climate)
    /// ✅ Pulls more candidates (configurable) then trims to maxItems after scoring
    ///
    /// Notes:
    /// - We STILL apply a DB LIMIT (configurable) to prevent scanning huge tables.
    ///   "No limit" is not safe for production; instead increase limits via config/env.
    /// </summary>
    public class LegalCommentaryRetrieverPg : ILegalCommentaryRetriever
    {
        private readonly ApplicationDbContext _db;

        private const string TsConfig = "english";

        // Sensible defaults; override via env vars if you want "not limited"
        private const int DEFAULT_MAX_LAWREPORT_CANDIDATES = 80;
        private const int DEFAULT_MAX_PAGE_CANDIDATES = 260;

        // Safety: don't return more than N pages per doc in final sources
        private const int MAX_PAGES_PER_DOC_IN_SOURCES = 2;

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

            // We intentionally retrieve MORE than maxItems, then trim after ranking.
            // You can raise these via env vars without code changes.
            var maxLawCandidates = GetIntEnv("AI_RETRIEVER_MAX_LAWREPORT_CANDIDATES", DEFAULT_MAX_LAWREPORT_CANDIDATES);
            var maxPageCandidates = GetIntEnv("AI_RETRIEVER_MAX_PAGE_CANDIDATES", DEFAULT_MAX_PAGE_CANDIDATES);

            // Scale with maxItems (but clamp)
            var lawLimit = Clamp(maxItems * 6, 24, maxLawCandidates);
            var pageLimit = Clamp(maxItems * 18, 60, maxPageCandidates);

            var lawReportCandidates = new List<(int Id, string Title, string Citation, string Text, double Rank)>();
            var pageCandidates = new List<(int LegalDocumentId, string DocTitle, int PageNumber, string Text, double Rank)>();

            var cs = _db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync(ct);

            // ------------------------------------------------------------
            // 1) LAW REPORTS (FTS)
            // ------------------------------------------------------------
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    r.""Id"" AS id,
    COALESCE(r.""Parties"", 'Law Report #' || r.""Id"") AS title,
    COALESCE(r.""Citation"", '') AS citation,
    COALESCE(r.""ContentText"", '') AS text,
    ts_rank(
        to_tsvector(@cfg,
            COALESCE(r.""Parties"", '') || ' ' ||
            COALESCE(r.""Citation"", '') || ' ' ||
            COALESCE(r.""Court"", '') || ' ' ||
            COALESCE(r.""Judges"", '') || ' ' ||
            COALESCE(r.""ContentText"", '')
        ),
        websearch_to_tsquery(@cfg, @q)
    ) AS rank
FROM ""LawReports"" r
WHERE to_tsvector(@cfg,
        COALESCE(r.""Parties"", '') || ' ' ||
        COALESCE(r.""Citation"", '') || ' ' ||
        COALESCE(r.""Court"", '') || ' ' ||
        COALESCE(r.""Judges"", '') || ' ' ||
        COALESCE(r.""ContentText"", '')
    ) @@ websearch_to_tsquery(@cfg, @q)
ORDER BY rank DESC, r.""Id"" DESC
LIMIT @limit;
";

                cmd.Parameters.Add(new NpgsqlParameter("cfg", TsConfig));
                cmd.Parameters.Add(new NpgsqlParameter("q", q));
                cmd.Parameters.Add(new NpgsqlParameter("limit", lawLimit));

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetInt32(reader.GetOrdinal("id"));
                    var title = reader.GetString(reader.GetOrdinal("title"));
                    var citation = reader.GetString(reader.GetOrdinal("citation"));
                    var text = reader.GetString(reader.GetOrdinal("text"));
                    var rank = Convert.ToDouble(reader["rank"]);

                    lawReportCandidates.Add((id, title, citation, text, rank));
                }
            }

            // ------------------------------------------------------------
            // 2) LEGAL DOCUMENT PAGE TEXTS (FTS with title join)
            // ------------------------------------------------------------
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    p.""LegalDocumentId"" AS doc_id,
    p.""PageNumber"" AS page_no,
    COALESCE(p.""Text"", '') AS text,
    COALESCE(d.""Title"", 'LegalDocument #' || p.""LegalDocumentId"") AS doc_title,
    ts_rank(
        to_tsvector(@cfg, COALESCE(p.""Text"", '')),
        websearch_to_tsquery(@cfg, @q)
    ) AS rank
FROM ""LegalDocumentPageTexts"" p
JOIN ""LegalDocuments"" d ON d.""Id"" = p.""LegalDocumentId""
WHERE to_tsvector(@cfg, COALESCE(p.""Text"", ''))
    @@ websearch_to_tsquery(@cfg, @q)
ORDER BY rank DESC, p.""LegalDocumentId"" DESC, p.""PageNumber"" ASC
LIMIT @limit;
";

                cmd.Parameters.Add(new NpgsqlParameter("cfg", TsConfig));
                cmd.Parameters.Add(new NpgsqlParameter("q", q));
                cmd.Parameters.Add(new NpgsqlParameter("limit", pageLimit)); // ✅ FIX: was lawLimit before

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var docId = reader.GetInt32(reader.GetOrdinal("doc_id"));
                    var pageNo = reader.GetInt32(reader.GetOrdinal("page_no"));
                    var text = reader.GetString(reader.GetOrdinal("text"));
                    var docTitle = reader.GetString(reader.GetOrdinal("doc_title"));
                    var rank = Convert.ToDouble(reader["rank"]);

                    pageCandidates.Add((docId, docTitle, pageNo, text, rank));
                }
            }

            // ------------------------------------------------------------
            // 2b) Fallback for OCR / "glued words" when FTS returns 0
            // ------------------------------------------------------------
            // Many OCR extractions produce tokens like "2016Climate" which FTS may not match.
            // Fallback uses ILIKE to at least return something (still limited & ordered).
            if (pageCandidates.Count == 0)
            {
                // Keep fallback query shorter (avoid massive wildcard query)
                var fallbackQ = BuildFallbackQuery(q);

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT
    p.""LegalDocumentId"" AS doc_id,
    p.""PageNumber"" AS page_no,
    COALESCE(p.""Text"", '') AS text,
    COALESCE(d.""Title"", 'LegalDocument #' || p.""LegalDocumentId"") AS doc_title,
    0.01 AS rank
FROM ""LegalDocumentPageTexts"" p
JOIN ""LegalDocuments"" d ON d.""Id"" = p.""LegalDocumentId""
WHERE COALESCE(p.""Text"", '') ILIKE '%' || @q || '%'
ORDER BY p.""LegalDocumentId"" DESC, p.""PageNumber"" ASC
LIMIT @limit;
";

                    cmd.Parameters.Add(new NpgsqlParameter("q", fallbackQ));
                    cmd.Parameters.Add(new NpgsqlParameter("limit", pageLimit));

                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        var docId = reader.GetInt32(reader.GetOrdinal("doc_id"));
                        var pageNo = reader.GetInt32(reader.GetOrdinal("page_no"));
                        var text = reader.GetString(reader.GetOrdinal("text"));
                        var docTitle = reader.GetString(reader.GetOrdinal("doc_title"));
                        var rank = Convert.ToDouble(reader["rank"]);

                        pageCandidates.Add((docId, docTitle, pageNo, text, rank));
                    }
                }
            }

            // ------------------------------------------------------------
            // 3) Build sources + grounding
            // ------------------------------------------------------------
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
                    Score = r.Rank
                });
            }

            // max N pages per doc (avoid stuffing many pages of same doc)
            var perDocCount = new Dictionary<int, int>();
            foreach (var p in pageCandidates.OrderByDescending(x => x.Rank))
            {
                perDocCount.TryGetValue(p.LegalDocumentId, out var c);
                if (c >= MAX_PAGES_PER_DOC_IN_SOURCES) continue;
                perDocCount[p.LegalDocumentId] = c + 1;

                sources.Add(new LegalCommentarySourceDto
                {
                    Type = "pdf_page", // keep existing type to avoid breaking any client expectations
                    LegalDocumentId = p.LegalDocumentId,
                    PageNumber = p.PageNumber,
                    Title = p.DocTitle,
                    Citation = $"Page {p.PageNumber}",
                    Snippet = MakeSnippet(p.Text, q),
                    Score = p.Rank
                });
            }

            var limited = sources
                .OrderByDescending(s => s.Score)
                .Take(Math.Max(1, maxItems))
                .ToList();

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
                    grounding.Add($"[PDF_PAGE:DOC={s.LegalDocumentId}:PAGE={s.PageNumber}] {s.Title}".Trim());
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

        private static int GetIntEnv(string key, int fallback)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            return int.TryParse(raw, out var n) && n > 0 ? n : fallback;
        }

        private static int Clamp(int value, int min, int max)
            => Math.Max(min, Math.Min(max, value));

        /// <summary>
        /// Build a safer fallback query string (for ILIKE) by extracting a few meaningful tokens.
        /// This prevents running ILIKE on a huge question string.
        /// </summary>
        private static string BuildFallbackQuery(string question)
        {
            var tokens = (question ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length >= 4)
                .Take(6)
                .ToArray();

            if (tokens.Length == 0)
                return (question ?? "").Trim();

            // Join tokens to increase hit chances for OCR-glued text
            return string.Join(" ", tokens);
        }

        private static string MakeSnippet(string text, string question)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var t = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length <= 520) return t;

            var tokens = (question ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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