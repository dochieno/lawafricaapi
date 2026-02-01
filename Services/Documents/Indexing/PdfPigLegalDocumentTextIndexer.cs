using System.Text;
using LawAfrica.API.Data;
using LawAfrica.API.Models.Documents;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace LawAfrica.API.Services.Documents.Indexing
{
    public interface ILegalDocumentTextIndexer
    {
        Task<IndexResult> IndexAsync(int legalDocumentId, bool force, CancellationToken ct);
    }

    public sealed class IndexResult
    {
        public int LegalDocumentId { get; init; }
        public bool Skipped { get; init; }
        public string? SkipReason { get; init; }

        public int PagesTotal { get; init; }
        public int PagesIndexed { get; init; }
        public int PagesEmptyText { get; init; }

        public List<int> EmptyTextPages { get; init; } = new();
    }

    public sealed class PdfPigLegalDocumentTextIndexer : ILegalDocumentTextIndexer
    {
        private readonly ApplicationDbContext _db;

        // Optional root if FilePath is relative (set env var)
        private readonly string _storageRoot;

        public PdfPigLegalDocumentTextIndexer(ApplicationDbContext db)
        {
            _db = db;
            _storageRoot = Environment.GetEnvironmentVariable("DOCUMENT_STORAGE_ROOT") ?? "";
        }

        public async Task<IndexResult> IndexAsync(int legalDocumentId, bool force, CancellationToken ct)
        {
            var doc = await _db.LegalDocuments
                .FirstOrDefaultAsync(x => x.Id == legalDocumentId, ct);

            if (doc == null)
            {
                return new IndexResult
                {
                    LegalDocumentId = legalDocumentId,
                    Skipped = true,
                    SkipReason = "Document not found."
                };
            }

            if (!string.Equals(doc.FileType, "pdf", StringComparison.OrdinalIgnoreCase))
            {
                return new IndexResult
                {
                    LegalDocumentId = legalDocumentId,
                    Skipped = true,
                    SkipReason = "Not a PDF document."
                };
            }

            // If already indexed and not forcing, skip (you can refine this later with FileHashSha256 checks)
            if (!force && doc.LastIndexedAt.HasValue)
            {
                return new IndexResult
                {
                    LegalDocumentId = legalDocumentId,
                    Skipped = true,
                    SkipReason = $"Already indexed at {doc.LastIndexedAt:O}."
                };
            }

            var path = ResolvePath(doc.FilePath);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new IndexResult
                {
                    LegalDocumentId = legalDocumentId,
                    Skipped = true,
                    SkipReason = "PDF file not found on server storage (FilePath)."
                };
            }

            // Extract
            using var pdf = PdfDocument.Open(path);

            var total = pdf.NumberOfPages;
            var pagesIndexed = 0;
            var pagesEmpty = 0;
            var emptyPages = new List<int>();

            // Optional: pre-load existing page rows for quick upsert
            var existing = await _db.LegalDocumentPageTexts
                .Where(x => x.LegalDocumentId == legalDocumentId)
                .ToDictionaryAsync(x => x.PageNumber, ct);

            for (int p = 1; p <= total; p++)
            {
                ct.ThrowIfCancellationRequested();

                var page = pdf.GetPage(p);
                var raw = page?.Text ?? string.Empty;

                var normalized = NormalizeText(raw);

                if (string.IsNullOrWhiteSpace(normalized))
                {
                    pagesEmpty++;
                    emptyPages.Add(p);
                    // still store empty? recommended: yes, to mark it processed
                    normalized = string.Empty;
                }

                if (existing.TryGetValue(p, out var row))
                {
                    // Only update if changed (reduces churn)
                    if (!string.Equals(row.Text, normalized, StringComparison.Ordinal))
                    {
                        row.Text = normalized;
                        row.UpdatedAt = DateTime.UtcNow;
                    }
                }
                else
                {
                    _db.LegalDocumentPageTexts.Add(new LegalDocumentPageText
                    {
                        LegalDocumentId = legalDocumentId,
                        PageNumber = p,
                        Text = normalized,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                pagesIndexed++;

                // Save in batches (important for large PDFs)
                if (p % 25 == 0)
                {
                    await _db.SaveChangesAsync(ct);
                }
            }

            // Final save
            doc.PageCount = doc.PageCount ?? total;
            doc.LastIndexedAt = DateTime.UtcNow;
            doc.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return new IndexResult
            {
                LegalDocumentId = legalDocumentId,
                PagesTotal = total,
                PagesIndexed = pagesIndexed,
                PagesEmptyText = pagesEmpty,
                EmptyTextPages = emptyPages
            };
        }

        private string ResolvePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return filePath;

            // If already absolute
            if (Path.IsPathRooted(filePath)) return filePath;

            // Otherwise join with configured root
            if (string.IsNullOrWhiteSpace(_storageRoot)) return filePath;

            return Path.Combine(_storageRoot, filePath.TrimStart('/', '\\'));
        }

        private static string NormalizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // Keep it simple: trim + collapse repeated whitespace a bit
            var sb = new StringBuilder(input.Length);
            bool lastWasWs = false;

            foreach (var ch in input)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!lastWasWs)
                    {
                        sb.Append(' ');
                        lastWasWs = true;
                    }
                    continue;
                }

                sb.Append(ch);
                lastWasWs = false;
            }

            return sb.ToString().Trim();
        }
    }
}
