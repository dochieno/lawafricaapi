using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Reports;
using LawAfrica.API.Models;
using LawAfrica.API.Models.LawReports.Enums;
using LawAfrica.API.Models.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/law-reports")]
    public class LawReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        // ✅ enum (matches LegalDocument.Category type)
        private const LegalDocumentCategory LLR_CATEGORY = LegalDocumentCategory.LLRServices;

        public LawReportsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // -------------------------
        // GET single
        // -------------------------
        [HttpGet("{id:int}")]
        public async Task<ActionResult<LawReportDto>> Get(int id)
        {
            var r = await _db.LawReports
                .Include(x => x.LegalDocument)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (r == null) return NotFound();

            return new LawReportDto
            {
                Id = r.Id,
                LegalDocumentId = r.LegalDocumentId,

                CountryId = r.CountryId,
                Service = r.Service,

                ReportNumber = r.ReportNumber,
                Year = r.Year,
                CaseNumber = r.CaseNumber,
                Citation = r.Citation,
                DecisionType = r.DecisionType,
                CaseType = r.CaseType,
                Court = r.Court,
                Parties = r.Parties,
                Judges = r.Judges,
                DecisionDate = r.DecisionDate,
                ContentText = r.ContentText,
                Title = r.LegalDocument?.Title ?? ""
            };
        }

        // -------------------------
        // ADMIN LIST
        // -------------------------
        [Authorize(Roles = "Admin")]
        [HttpGet("admin")]
        public async Task<ActionResult<List<LawReportDto>>> AdminList([FromQuery] string? q = null)
        {
            q = (q ?? "").Trim();

            try
            {
                var query = _db.LawReports
                    .AsNoTracking()
                    .Include(x => x.LegalDocument)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(q))
                {
                    query = query.Where(r =>
                        (r.ReportNumber != null && r.ReportNumber.Contains(q)) ||
                        (r.Citation != null && r.Citation.Contains(q)) ||
                        (r.CaseNumber != null && r.CaseNumber.Contains(q)) ||
                        (r.Parties != null && r.Parties.Contains(q)) ||
                        (r.Court != null && r.Court.Contains(q)) ||
                        (r.Judges != null && r.Judges.Contains(q)) ||
                        (r.LegalDocument != null && r.LegalDocument.Title != null && r.LegalDocument.Title.Contains(q))
                    );
                }

                var list = await query
                    .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
                    .ThenByDescending(r => r.Id)
                    .Select(r => new LawReportDto
                    {
                        Id = r.Id,
                        LegalDocumentId = r.LegalDocumentId,

                        CountryId = r.CountryId,
                        Service = r.Service,

                        ReportNumber = r.ReportNumber,
                        Year = r.Year,
                        CaseNumber = r.CaseNumber,
                        Citation = r.Citation,
                        DecisionType = r.DecisionType,
                        CaseType = r.CaseType,
                        Court = r.Court,
                        Parties = r.Parties,
                        Judges = r.Judges,
                        DecisionDate = r.DecisionDate,

                        ContentText = "",

                        // ✅ avoid NullReference
                        Title = r.LegalDocument != null ? r.LegalDocument.Title : ""
                    })
                    .ToListAsync();

                return Ok(list);
            }
            catch (Exception ex)
            {
                // Return something you can actually read on the frontend
                return StatusCode(500, new
                {
                    message = "Failed to load law reports (admin).",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }


        // -------------------------
        // CREATE
        // -------------------------
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<ActionResult<LawReportDto>> Create([FromBody] LawReportUpsertDto dto)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var existing = await FindExistingByDedupe(dto);
            if (existing != null)
                return Conflict(new { message = "Duplicate report exists.", existingLawReportId = existing.Id });

            var title = BuildReportTitle(dto);

            var doc = new LegalDocument
            {
                Title = title,
                Description = $"Law Report {dto.ReportNumber} ({dto.Year})",

                Category = LLR_CATEGORY,
                CountryId = dto.CountryId,

                FilePath = "",
                FileType = "report",
                FileSizeBytes = 0,

                IsPremium = true,
                Version = "1",
                Status = LegalDocumentStatus.Published,
                AllowPublicPurchase = true,
                Kind = LegalDocumentKind.Report,
                PublicCurrency = "KES",
                PublicPrice = 0,
                CreatedAt = DateTime.UtcNow,
                PublishedAt = DateTime.UtcNow

            };

            _db.LegalDocuments.Add(doc);
            await _db.SaveChangesAsync();

            var report = new LawReport
            {
                LegalDocumentId = doc.Id,

                CountryId = dto.CountryId,
                Service = dto.Service,

                Citation = TrimOrNull(dto.Citation),
                ReportNumber = dto.ReportNumber.Trim(),
                Year = dto.Year,
                CaseNumber = TrimOrNull(dto.CaseNumber),
                DecisionType = dto.DecisionType,
                CaseType = dto.CaseType,
                Court = TrimOrNull(dto.Court),
                Parties = TrimOrNull(dto.Parties),
                Judges = TrimOrNull(dto.Judges),
                DecisionDate = dto.DecisionDate,
                ContentText = dto.ContentText,
                CreatedAt = DateTime.UtcNow
            };

            _db.LawReports.Add(report);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(Get), new { id = report.Id }, new LawReportDto
            {
                Id = report.Id,
                LegalDocumentId = report.LegalDocumentId,

                CountryId = report.CountryId,
                Service = report.Service,

                ReportNumber = report.ReportNumber,
                Year = report.Year,
                CaseNumber = report.CaseNumber,
                Citation = report.Citation,
                DecisionType = report.DecisionType,
                CaseType = report.CaseType,
                Court = report.Court,
                Parties = report.Parties,
                Judges = report.Judges,
                DecisionDate = report.DecisionDate,
                ContentText = report.ContentText,
                Title = doc.Title
            });
        }

        // -------------------------
        // UPDATE
        // -------------------------
        [Authorize(Roles = "Admin")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] LawReportUpsertDto dto)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var r = await _db.LawReports
                .Include(x => x.LegalDocument)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (r == null) return NotFound();

            var existing = await FindExistingByDedupe(dto);
            if (existing != null && existing.Id != id)
                return Conflict(new { message = "Duplicate report exists.", existingLawReportId = existing.Id });

            r.CountryId = dto.CountryId;
            r.Service = dto.Service;

            r.Citation = TrimOrNull(dto.Citation);
            r.ReportNumber = dto.ReportNumber.Trim();
            r.Year = dto.Year;
            r.CaseNumber = TrimOrNull(dto.CaseNumber);
            r.DecisionType = dto.DecisionType;
            r.CaseType = dto.CaseType;
            r.Court = TrimOrNull(dto.Court);
            r.Parties = TrimOrNull(dto.Parties);
            r.Judges = TrimOrNull(dto.Judges);
            r.DecisionDate = dto.DecisionDate;
            r.ContentText = dto.ContentText;
            r.UpdatedAt = DateTime.UtcNow;

            if (r.LegalDocument != null)
            {
                r.LegalDocument.Title = BuildReportTitle(dto);
                r.LegalDocument.Description = $"Law Report {dto.ReportNumber} ({dto.Year})";

                r.LegalDocument.Category = LLR_CATEGORY;
                r.LegalDocument.CountryId = dto.CountryId;

                r.LegalDocument.Kind = LegalDocumentKind.Report;
                r.LegalDocument.FileType = "report";
                r.LegalDocument.FilePath = r.LegalDocument.FilePath ?? "";
                r.LegalDocument.Version = string.IsNullOrWhiteSpace(r.LegalDocument.Version) ? "1" : r.LegalDocument.Version;
                r.LegalDocument.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // -------------------------
        // DELETE
        // -------------------------
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var r = await _db.LawReports.FirstOrDefaultAsync(x => x.Id == id);
            if (r == null) return NotFound();

            var doc = await _db.LegalDocuments.FindAsync(r.LegalDocumentId);
            if (doc != null) _db.LegalDocuments.Remove(doc);

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // -------------------------
        // helpers
        // -------------------------
        private async Task<LawReport?> FindExistingByDedupe(LawReportUpsertDto dto)
        {
            var citation = TrimOrNull(dto.Citation);
            var reportNumber = dto.ReportNumber.Trim();
            var caseNo = TrimOrNull(dto.CaseNumber);

            if (!string.IsNullOrWhiteSpace(citation))
            {
                var byCitation = await _db.LawReports.FirstOrDefaultAsync(x => x.Citation == citation);
                if (byCitation != null) return byCitation;
            }

            return await _db.LawReports.FirstOrDefaultAsync(x =>
                x.ReportNumber == reportNumber &&
                x.Year == dto.Year &&
                x.CaseNumber == caseNo);
        }

        private static string BuildReportTitle(LawReportUpsertDto dto)
        {
            var parts = new List<string> { $"{dto.ReportNumber.Trim()} ({dto.Year})" };

            if (!string.IsNullOrWhiteSpace(dto.Parties))
                parts.Add(dto.Parties.Trim());

            if (!string.IsNullOrWhiteSpace(dto.Citation))
                parts.Add(dto.Citation.Trim());

            return string.Join(" - ", parts);
        }

        private static string? TrimOrNull(string? s)
        {
            var t = (s ?? "").Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }
    }
}
