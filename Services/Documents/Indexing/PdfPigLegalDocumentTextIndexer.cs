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
            _storageRoot = Environment.GetEnvironmentVariable("STORAGE_ROOT") ?? "";
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

            // If already indexed AND we actually have page text rows, skip (unless forcing)
            if (!force && doc.LastIndexedAt.HasValue)
            {
                var existingCount = await _db.LegalDocumentPageTexts
                    .CountAsync(x => x.LegalDocumentId == legalDocumentId, ct);

                if (existingCount > 0)
                {
                    return new IndexResult
                    {
                        LegalDocumentId = legalDocumentId,
                        Skipped = true,
                        SkipReason = $"Already indexed at {doc.LastIndexedAt:O} (pageTextRows={existingCount})."
                    };
                }
                // else: LastIndexedAt is set but rows are missing → proceed to rebuild.
            }

            var resolved = ResolvePath(doc.FilePath);

            if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
            {
                return new IndexResult
                {
                    LegalDocumentId = legalDocumentId,
                    Skipped = true,
                    SkipReason = $"PDF file not found on server storage (FilePath). ResolvedPath='{resolved ?? ""}'."
                };
            }

            // If forcing, wipe existing extracted pages first (clean rebuild)
            if (force)
            {
                await _db.LegalDocumentPageTexts
                    .Where(x => x.LegalDocumentId == legalDocumentId)
                    .ExecuteDeleteAsync(ct);
            }

            using var pdf = PdfDocument.Open(resolved);

            var total = pdf.NumberOfPages;
            var pagesIndexed = 0;
            var pagesEmpty = 0;
            var emptyPages = new List<int>();

            // Pre-load existing page rows for quick upsert (when not force)
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
                    normalized = string.Empty; // still mark page as processed
                }

                if (existing.TryGetValue(p, out var row))
                {
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

            // Normalize slashes
            var fp = filePath.Trim().Replace('\\', '/');

            // Absolute path: use as-is
            if (Path.IsPathRooted(fp)) return fp;

            // If filePath begins with "Storage/", strip it when combining with STORAGE_ROOT that already points to ".../Storage"
            // Example:
            //   STORAGE_ROOT = "/app/Storage"
            //   FilePath     = "Storage/LegalDocuments/x.pdf"
            // We want:
            //   "/app/Storage/LegalDocuments/x.pdf"  (not "/app/Storage/Storage/...")
            if (fp.StartsWith("storage/", StringComparison.OrdinalIgnoreCase))
            {
                fp = fp.Substring("storage/".Length);
            }

            // Prefer STORAGE_ROOT when set
            if (!string.IsNullOrWhiteSpace(_storageRoot))
            {
                var root = _storageRoot.Trim().Replace('\\', '/').TrimEnd('/');

                // If root itself ends with "/Storage" and fp still starts with "Storage/", strip again (extra safety)
                if (fp.StartsWith("storage/", StringComparison.OrdinalIgnoreCase))
                {
                    fp = fp.Substring("storage/".Length);
                }

                return Path.Combine(root, fp.Replace('/', Path.DirectorySeparatorChar));
            }

            // Fallback: combine with current directory
            return Path.Combine(Directory.GetCurrentDirectory(), fp.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string NormalizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

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
