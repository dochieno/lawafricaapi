using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Reports;
using LawAfrica.API.Models;
using LawAfrica.API.Models.LawReports.Enums;
using LawAfrica.API.Models.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/law-reports")]
    public class LawReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public LawReportsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // -------------------------
        // CRUD
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
                Title = r.LegalDocument.Title
            };
        }

        // ...

        // -------------------------
        // Report Content (by LegalDocumentId)
        // -------------------------

        // ✅ Admin page will call this using LegalDocumentId
        [Authorize(Roles = "Admin")]
        [HttpGet("by-document/{legalDocumentId:int}/content")]
        public async Task<ActionResult<LawReportContentDto>> GetContentByLegalDocumentId(int legalDocumentId)
        {
            var r = await _db.LawReports
                .Include(x => x.LegalDocument)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.LegalDocumentId == legalDocumentId);

            if (r == null) return NotFound(new { message = "LawReport not found for this LegalDocumentId." });

            // safety: ensure it's actually a Report kind
            if (r.LegalDocument == null || r.LegalDocument.Kind != LegalDocumentKind.Report)
                return BadRequest(new { message = "LegalDocument is not of kind Report." });

            return Ok(new LawReportContentDto
            {
                LawReportId = r.Id,
                LegalDocumentId = r.LegalDocumentId,
                Title = r.LegalDocument.Title,
                ContentText = r.ContentText ?? "",
                UpdatedAt = r.UpdatedAt
            });
        }

    // ✅ UPSERT (practically Update) report content by LegalDocumentId
    // If the LawReport row is missing, we return 404 because we can't create
    // a valid LawReport without required metadata (ReportNumber/Year/etc).
    [Authorize(Roles = "Admin")]
    [HttpPut("by-document/{legalDocumentId:int}/content")]
    public async Task<IActionResult> UpsertContentByLegalDocumentId(
        int legalDocumentId,
        [FromBody] LawReportContentUpsertDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var r = await _db.LawReports
            .Include(x => x.LegalDocument)
            .FirstOrDefaultAsync(x => x.LegalDocumentId == legalDocumentId);

        if (r == null)
            return NotFound(new { message = "LawReport not found for this LegalDocumentId." });

        if (r.LegalDocument == null || r.LegalDocument.Kind != LegalDocumentKind.Report)
            return BadRequest(new { message = "LegalDocument is not of kind Report." });

        r.ContentText = (dto.ContentText ?? "").Trim();
        r.UpdatedAt = DateTime.UtcNow;

        // optional: also bump parent UpdatedAt
        r.LegalDocument.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return NoContent();
    }


    // ✅ Admin only for writing (adjust to your permission system)
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<ActionResult<LawReportDto>> Create([FromBody] LawReportUpsertDto dto)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            // Dedupe check
            var existing = await FindExistingByDedupe(dto);
            if (existing != null)
                return Conflict(new { message = "Duplicate report exists.", existingLawReportId = existing.Id });

            // Create LegalDocument parent
            var title = BuildReportTitle(dto);

            var doc = new LegalDocument
            {
                Title = title,
                Description = $"Law Report {dto.ReportNumber} ({dto.Year})",
                Kind = LegalDocumentKind.Report,

                // Reports are text-based; keep FilePath empty
                FilePath = "",
                FileType = "report",
                FileSizeBytes = 0,
                PageCount = null,
                ChapterCount = null,

                // business flags
                IsPremium = true,
                Status = LegalDocumentStatus.Published,
                PublishedAt = DateTime.UtcNow
            };

            _db.LegalDocuments.Add(doc);
            await _db.SaveChangesAsync();

            var report = new LawReport
            {
                LegalDocumentId = doc.Id,
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
                ContentText = dto.ContentText
            };

            _db.LawReports.Add(report);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(Get), new { id = report.Id }, new LawReportDto
            {
                Id = report.Id,
                LegalDocumentId = report.LegalDocumentId,
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

        [Authorize(Roles = "Admin")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] LawReportUpsertDto dto)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var r = await _db.LawReports
                .Include(x => x.LegalDocument)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (r == null) return NotFound();

            // If keys changed, ensure no collision
            var existing = await FindExistingByDedupe(dto);
            if (existing != null && existing.Id != id)
                return Conflict(new { message = "Duplicate report exists.", existingLawReportId = existing.Id });

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

            // Keep parent title in sync
            r.LegalDocument.Title = BuildReportTitle(dto);
            r.LegalDocument.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ✅ Admin list (used by AdminLLRServices UI)
        [Authorize(Roles = "Admin")]
        [HttpGet("admin")]
        public async Task<ActionResult<List<LawReportDto>>> AdminList([FromQuery] string? q = null)
        {
            q = (q ?? "").Trim();

            var query = _db.LawReports
                .AsNoTracking()
                .Include(x => x.LegalDocument)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                // simple contains search (safe; optimize later)
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
                    ContentText = null!, // ✅ keep list light (Admin UI loads full report on Edit/Content)
                    Title = r.LegalDocument.Title
                })
                .ToListAsync();

            return Ok(list);
        }


        [Authorize(Roles = "Admin")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var r = await _db.LawReports.FirstOrDefaultAsync(x => x.Id == id);
            if (r == null) return NotFound();

            // cascade delete will remove LawReport when LegalDocument deleted,
            // but we want to delete the parent to keep catalog clean
            var doc = await _db.LegalDocuments.FindAsync(r.LegalDocumentId);
            if (doc != null) _db.LegalDocuments.Remove(doc);

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // -------------------------
        // Reader helpers
        // -------------------------

        [HttpGet("{id:int}/search")]
        public async Task<ActionResult<object>> SearchInReport(int id, [FromQuery] string q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return BadRequest(new { message = "q must be at least 2 characters." });

            var r = await _db.LawReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (r == null) return NotFound();

            // very simple search (upgrade later to full text)
            var text = r.ContentText ?? "";
            var hits = new List<object>();

            var idx = 0;
            var maxHits = 50;
            while (hits.Count < maxHits)
            {
                idx = text.IndexOf(q, idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;

                var start = Math.Max(0, idx - 80);
                var end = Math.Min(text.Length, idx + q.Length + 80);
                var snippet = text.Substring(start, end - start);

                hits.Add(new { index = idx, snippet });
                idx += q.Length;
            }

            return Ok(new { query = q, count = hits.Count, hits });
        }

        // -------------------------
        // IMPORT PREVIEW/CONFIRM
        // -------------------------

        [Authorize(Roles = "Admin")]
        [HttpPost("import/excel")]
        [RequestSizeLimit(50_000_000)]
        public async Task<ActionResult<ReportImportPreviewDto>> ImportExcelPreview([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("Excel file is required.");

            // ✅ Parse rows (implement parser below; keeps controller clean)
            var items = await ReportImportParsers.ParseExcelAsync(file);

            var preview = await BuildPreview(items);
            return Ok(preview);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("import/word")]
        [RequestSizeLimit(50_000_000)]
        public async Task<ActionResult<ReportImportPreviewDto>> ImportWordPreview([FromForm] IFormFile file, [FromForm] string reportNumber, [FromForm] int year)
        {
            if (file == null || file.Length == 0) return BadRequest("Word file is required.");

            // ✅ Minimal assumption: Word contains content text, metadata provided in form fields
            var item = await ReportImportParsers.ParseWordAsync(file, reportNumber, year);

            var preview = await BuildPreview(new List<ReportImportPreviewItemDto> { item });
            return Ok(preview);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("import/confirm")]
        public async Task<ActionResult<ReportImportConfirmResult>> ImportConfirm([FromBody] ReportImportConfirmRequest req)
        {
            if (req.Items == null || req.Items.Count == 0)
                return BadRequest("No items provided.");

            var result = new ReportImportConfirmResult();

            foreach (var dto in req.Items)
            {
                // validate DTO rules
                var ctx = new ValidationContext(dto);
                var valResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
                if (!Validator.TryValidateObject(dto, ctx, valResults, validateAllProperties: true))
                {
                    result.Errors.Add($"Invalid item {dto.ReportNumber}/{dto.Year}: {string.Join("; ", valResults.Select(v => v.ErrorMessage))}");
                    continue;
                }

                var existing = await FindExistingByDedupe(dto);

                if (existing != null)
                {
                    if (req.DuplicateStrategy == ImportDuplicateStrategy.Skip)
                    {
                        result.Skipped++;
                        continue;
                    }

                    // Update existing
                    existing.Citation = TrimOrNull(dto.Citation);
                    existing.ReportNumber = dto.ReportNumber.Trim();
                    existing.Year = dto.Year;
                    existing.CaseNumber = TrimOrNull(dto.CaseNumber);
                    existing.DecisionType = dto.DecisionType;
                    existing.CaseType = dto.CaseType;
                    existing.Court = TrimOrNull(dto.Court);
                    existing.Parties = TrimOrNull(dto.Parties);
                    existing.Judges = TrimOrNull(dto.Judges);
                    existing.DecisionDate = dto.DecisionDate;
                    existing.ContentText = dto.ContentText;
                    existing.UpdatedAt = DateTime.UtcNow;

                    // keep parent title synced
                    var doc = await _db.LegalDocuments.FindAsync(existing.LegalDocumentId);
                    if (doc != null)
                    {
                        doc.Title = BuildReportTitle(dto);
                        doc.Kind = LegalDocumentKind.Report;
                        doc.IsPremium = true;
                        doc.FileType = "report";
                        doc.FilePath = "";
                        doc.UpdatedAt = DateTime.UtcNow;
                    }

                    result.Updated++;
                    await _db.SaveChangesAsync();
                    continue;
                }

                // Create new (LegalDocument + LawReport)
                var title = BuildReportTitle(dto);

                var docNew = new LegalDocument
                {
                    Title = title,
                    Description = $"Law Report {dto.ReportNumber} ({dto.Year})",
                    Kind = LegalDocumentKind.Report,
                    FilePath = "",
                    FileType = "report",
                    FileSizeBytes = 0,
                    IsPremium = true,
                    Status = LegalDocumentStatus.Published,
                    PublishedAt = DateTime.UtcNow
                };
                _db.LegalDocuments.Add(docNew);
                await _db.SaveChangesAsync();

                var reportNew = new LawReport
                {
                    LegalDocumentId = docNew.Id,
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
                    ContentText = dto.ContentText
                };
                _db.LawReports.Add(reportNew);
                await _db.SaveChangesAsync();

                result.Created++;
            }

            return Ok(result);
        }

        // -------------------------
        // helpers
        // -------------------------

        private async Task<LawReport?> FindExistingByDedupe(LawReportUpsertDto dto)
        {
            var citation = TrimOrNull(dto.Citation);
            var reportNumber = dto.ReportNumber.Trim();
            var caseNo = TrimOrNull(dto.CaseNumber);

            // If citation provided, prefer it
            if (!string.IsNullOrWhiteSpace(citation))
            {
                var byCitation = await _db.LawReports.FirstOrDefaultAsync(x => x.Citation == citation);
                if (byCitation != null) return byCitation;
            }

            // Otherwise use composite key
            return await _db.LawReports.FirstOrDefaultAsync(x =>
                x.ReportNumber == reportNumber &&
                x.Year == dto.Year &&
                x.CaseNumber == caseNo);
        }

        private async Task<ReportImportPreviewDto> BuildPreview(List<ReportImportPreviewItemDto> items)
        {
            // validate + dedupe mark
            foreach (var it in items)
            {
                it.Errors ??= new List<string>();

                if (!ReportValidation.IsValidReportNumber(it.ReportNumber))
                    it.Errors.Add("ReportNumber must start with 3 letters followed by digits (e.g. CAR353).");

                if (it.Year < 1900 || it.Year > 2100)
                    it.Errors.Add("Year must be between 1900 and 2100.");

                // DecisionType + CaseType come as raw strings from imports
                // we require them to match enum names
                if (!TryParseDecisionType(it.DecisionType, out _))
                    it.Errors.Add("DecisionType must be Judgment or Ruling.");

                if (!TryParseCaseType(it.CaseType, out _))
                    it.Errors.Add("CaseType must be Criminal/Civil/Environmental/Family/Commercial/Constitutional.");

                if (string.IsNullOrWhiteSpace(it.ContentText))
                    it.Errors.Add("ContentText is required.");

                // dedupe check
                var existing = await FindExistingByDedupe(new LawReportUpsertDto
                {
                    ReportNumber = it.ReportNumber,
                    Year = it.Year,
                    CaseNumber = it.CaseNumber,
                    Citation = it.Citation,
                    DecisionType = TryParseDecisionType(it.DecisionType, out var dt) ? dt : ReportDecisionType.Judgment,
                    CaseType = TryParseCaseType(it.CaseType, out var ct) ? ct : ReportCaseType.Civil,
                    Court = it.Court,
                    Parties = it.Parties,
                    Judges = it.Judges,
                    DecisionDate = it.DecisionDate,
                    ContentText = it.ContentText ?? ""
                });

                if (existing != null)
                {
                    it.IsDuplicate = true;
                    it.ExistingLawReportId = existing.Id;
                    it.ExistingLegalDocumentId = existing.LegalDocumentId;
                    it.DuplicateReason = !string.IsNullOrWhiteSpace(it.Citation) ? "Citation match" : "ReportNumber+Year+CaseNumber match";
                }

                it.IsValid = it.Errors.Count == 0;
            }

            return new ReportImportPreviewDto
            {
                Total = items.Count,
                Valid = items.Count(x => x.IsValid),
                Invalid = items.Count(x => !x.IsValid),
                Duplicates = items.Count(x => x.IsDuplicate),
                Items = items
            };
        }

        private static bool TryParseDecisionType(string? raw, out ReportDecisionType dt)
            => Enum.TryParse((raw ?? "").Trim(), ignoreCase: true, out dt)
               && (dt == ReportDecisionType.Judgment || dt == ReportDecisionType.Ruling);

        private static bool TryParseCaseType(string? raw, out ReportCaseType ct)
            => Enum.TryParse((raw ?? "").Trim(), ignoreCase: true, out ct);

        private static string BuildReportTitle(LawReportUpsertDto dto)
        {
            var parts = new List<string>
            {
                $"{dto.ReportNumber.Trim()} ({dto.Year})"
            };

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
