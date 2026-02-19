using LawAfrica.API.Data;
using LawAfrica.API.DTOs.AI.Commentary;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

            var lawLimit = Math.Max(3, Math.Max(1, maxItems / 2));
            var pageLimit = Math.Max(6, maxItems * 2);

            var lawReportCandidates = new List<(int Id, string Title, string Citation, string Text, double Rank)>();
            var pageCandidates = new List<(int LegalDocumentId, int PageNumber, string Text, double Rank)>();

            var cs = _db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync(ct);


            // ------------------------------------------------------------
            // 1) LAW REPORTS
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
                    to_tsvector('english',
                        COALESCE(r.""Parties"", '') || ' ' ||
                        COALESCE(r.""Citation"", '') || ' ' ||
                        COALESCE(r.""Court"", '') || ' ' ||
                        COALESCE(r.""Judges"", '') || ' ' ||
                        COALESCE(r.""ContentText"", '')
                    ),
                    plainto_tsquery('english', @q)
                ) AS rank
            FROM ""LawReports"" r
            WHERE to_tsvector('english',
                    COALESCE(r.""Parties"", '') || ' ' ||
                    COALESCE(r.""Citation"", '') || ' ' ||
                    COALESCE(r.""Court"", '') || ' ' ||
                    COALESCE(r.""Judges"", '') || ' ' ||
                    COALESCE(r.""ContentText"", '')
                ) @@ plainto_tsquery('english', @q)
            ORDER BY rank DESC, r.""Id"" DESC
            LIMIT @limit;
        ";

                var pQ = cmd.CreateParameter();
                pQ.ParameterName = "q";
                pQ.Value = q;
                cmd.Parameters.Add(pQ);

                var pLimit = cmd.CreateParameter();
                pLimit.ParameterName = "limit";
                pLimit.Value = lawLimit;
                cmd.Parameters.Add(pLimit);

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
            // 2) PDF PAGE TEXTS
            // ------------------------------------------------------------
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
            SELECT
                p.""LegalDocumentId"" AS doc_id,
                p.""PageNumber"" AS page_no,
                COALESCE(p.""Text"", '') AS text,
                ts_rank(
                    to_tsvector('english', COALESCE(p.""Text"", '')),
                    plainto_tsquery('english', @q)
                ) AS rank
            FROM ""LegalDocumentPageTexts"" p
            WHERE to_tsvector('english', COALESCE(p.""Text"", ''))
                @@ plainto_tsquery('english', @q)
            ORDER BY rank DESC, p.""LegalDocumentId"" DESC, p.""PageNumber"" ASC
            LIMIT @limit;
        ";

                cmd.Parameters.Add(new NpgsqlParameter("q", q));
                cmd.Parameters.Add(new NpgsqlParameter("limit", lawLimit));

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var docId = reader.GetInt32(reader.GetOrdinal("doc_id"));
                    var pageNo = reader.GetInt32(reader.GetOrdinal("page_no"));
                    var text = reader.GetString(reader.GetOrdinal("text"));
                    var rank = Convert.ToDouble(reader["rank"]);

                    pageCandidates.Add((docId, pageNo, text, rank));
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

            // max 2 pages per doc
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
