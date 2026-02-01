using System.Text;
using LawAfrica.API.Data;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Ai.Sections
{
    public class SectionTextExtractor : ISectionTextExtractor
    {
        private readonly ApplicationDbContext _db;

        // Safety clamp to avoid extreme payloads downstream
        private const int HARD_CHAR_LIMIT = 180_000;

        public SectionTextExtractor(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<SectionTextExtractionResult> ExtractAsync(
            int legalDocumentId,
            int startPage,
            int endPage,
            CancellationToken ct)
        {
            if (legalDocumentId <= 0 || startPage <= 0 || endPage <= 0)
            {
                return new SectionTextExtractionResult
                {
                    Text = string.Empty,
                    CharCount = 0,
                    PagesRequested = Math.Max(0, endPage - startPage + 1),
                    PagesFound = 0
                };
            }

            if (endPage < startPage)
                (startPage, endPage) = (endPage, startPage);

            var pagesRequested = endPage - startPage + 1;

            var rows = await _db.LegalDocumentPageTexts
                .AsNoTracking()
                .Where(x =>
                    x.LegalDocumentId == legalDocumentId &&
                    x.PageNumber >= startPage &&
                    x.PageNumber <= endPage)
                .OrderBy(x => x.PageNumber)
                .Select(x => new { x.PageNumber, x.Text })
                .ToListAsync(ct);

            if (rows.Count == 0)
            {
                return new SectionTextExtractionResult
                {
                    Text = string.Empty,
                    CharCount = 0,
                    PagesRequested = pagesRequested,
                    PagesFound = 0,
                    MissingPages = BuildMissingPages(startPage, endPage, new HashSet<int>())
                };
            }

            var foundPages = new HashSet<int>(rows.Select(r => r.PageNumber));
            var missing = BuildMissingPages(startPage, endPage, foundPages);

            var sb = new StringBuilder();

            foreach (var r in rows)
            {
                if (sb.Length >= HARD_CHAR_LIMIT) break;

                var t = (r.Text ?? string.Empty).Trim();
                if (t.Length == 0) continue;

                // Page markers keep the model grounded and help debugging
                sb.AppendLine($"[PAGE {r.PageNumber}]");
                AppendClamped(sb, t, HARD_CHAR_LIMIT);
                sb.AppendLine();
            }

            var finalText = sb.ToString().Trim();

            return new SectionTextExtractionResult
            {
                Text = finalText,
                CharCount = finalText.Length,
                PagesRequested = pagesRequested,
                PagesFound = foundPages.Count,
                MissingPages = missing
            };
        }

        private static void AppendClamped(StringBuilder sb, string chunk, int limit)
        {
            var remaining = limit - sb.Length;
            if (remaining <= 0) return;

            if (chunk.Length <= remaining) sb.AppendLine(chunk);
            else sb.AppendLine(chunk.Substring(0, remaining));
        }

        private static List<int> BuildMissingPages(int start, int end, HashSet<int> foundPages)
        {
            var missing = new List<int>();
            for (var p = start; p <= end; p++)
            {
                if (!foundPages.Contains(p)) missing.Add(p);
            }
            return missing;
        }
    }
}
