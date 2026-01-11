using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;
using System;
using UglyToad.PdfPig;

namespace LawAfrica.API.Services
{
    public class PdfDocumentIndexingService : IDocumentIndexingService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<PdfDocumentIndexingService> _logger;

        public PdfDocumentIndexingService(
            ApplicationDbContext db,
            ILogger<PdfDocumentIndexingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task IndexPdfAsync(LegalDocument document)
        {
            // Prevent re-indexing
            if (document.LastIndexedAt != null)
            {
                _logger.LogInformation(
                    "Document {Id} already indexed, skipping.", document.Id);
                return;
            }

            if (!File.Exists(document.FilePath))
            {
                _logger.LogError(
                    "PDF file not found for document {Id}", document.Id);
                return;
            }

            _logger.LogInformation(
                "Starting PDF indexing for document {Id}", document.Id);

            var fullPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    document.FilePath
                );

            if (!System.IO.File.Exists(fullPath))
            {
                _logger.LogError("PDF file not found at path: {Path}", fullPath);
                return;
            }

            using var pdf = PdfDocument.Open(fullPath);


            // Optional: clean existing index rows
            var existing = await _db.DocumentTextIndexes
                .Where(x => x.LegalDocumentId == document.Id)
                .ToListAsync();

            if (existing.Any())
            {
                _db.DocumentTextIndexes.RemoveRange(existing);
            }

            foreach (var page in pdf.GetPages())
            {
                _db.DocumentTextIndexes.Add(new DocumentTextIndex
                {
                    LegalDocumentId = document.Id,
                    PageNumber = page.Number,
                    Text = page.Text
                });
            }

            document.LastIndexedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Completed PDF indexing for document {Id}", document.Id);
        }
    }
}
