// =======================================================
// FILE: LawAfrica.API/Controllers/LawReports/LawReportsController.cs
// =======================================================
using System.Linq.Expressions;
using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Reports;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Models.LawReports.Enums;
using LawAfrica.API.Models.Locations;
using LawAfrica.API.Models.Reports;
using LawAfrica.API.Services.Documents;
using LawAfrica.API.Services.LawReports;
using LawAfrica.API.Services.LawReportsContent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/law-reports")]
    public class LawReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILawReportContentBuilder _builder;
        private readonly DocumentEntitlementService _entitlement;

        // ✅ enum (matches LegalDocument.Category type)
        private const LegalDocumentCategory LLR_CATEGORY = LegalDocumentCategory.LLRServices;

        public LawReportsController(
            ApplicationDbContext db,
            ILawReportContentBuilder builder,
            DocumentEntitlementService entitlement)
        {
            _db = db;
            _builder = builder;
            _entitlement = entitlement;
        }

        // -------------------------
        // GET single (GATED)
        [Authorize]
        [HttpGet("{id:int}")]
        public async Task<ActionResult<LawReportDto>> Get(int id, CancellationToken ct)
        {
            var r = await _db.LawReports
                .Include(x => x.LegalDocument)
                .Include(x => x.TownRef)
                .Include(x => x.CourtRef)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (r == null) return NotFound(new { message = "Law report not found." });

            if (r.LegalDocument == null)
                return StatusCode(500, new { message = "Law report is missing LegalDocument." });

            // ✅ Only gate published report docs
            if (r.LegalDocument.Status != LegalDocumentStatus.Published ||
                r.LegalDocument.Kind != LegalDocumentKind.Report ||
                r.LegalDocument.FileType != "report")
            {
                return NotFound(new { message = "Law report not available." });
            }

            var userId = User.GetUserId();

            // ✅ SINGLE SOURCE OF TRUTH for access
            var decision = await _entitlement.GetEntitlementDecisionAsync(userId, r.LegalDocument);

            // Hard blocks => return blocked meta, no content
            var isHardBlocked =
                !decision.IsAllowed &&
                (decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSubscriptionInactive ||
                 decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSeatLimitExceeded);

            if (isHardBlocked)
            {
                var dto = ToDto(r, includeContent: false);

                dto.ContentText = ""; // no preview on hard block
                ApplyEntitlementMeta(dto, decision, previewMaxChars: null, previewMaxParagraphs: null);

                dto.IsBlocked = true;
                dto.BlockReason = decision.DenyReason.ToString();
                dto.BlockMessage = decision.Message ?? "Access blocked. Please contact your administrator.";

                dto.HardStop = true; // stop user completely
                dto.AccessLevel = "PreviewOnly";

                return Ok(dto);
            }

            // Full access => return full content
            if (decision.AccessLevel == DocumentAccessLevel.FullAccess)
            {
                var dto = ToDto(r, includeContent: true);
                dto.AccessLevel = "FullAccess";
                dto.HardStop = false;
                dto.IsBlocked = false;

                ApplyEntitlementMeta(dto, decision, previewMaxChars: null, previewMaxParagraphs: null);
                return Ok(dto);
            }

            // Preview-only => truncate server-side
            var (maxChars, maxParas) = GetPreviewLimits(decision);

            var raw = r.ContentText ?? "";
            var preview = ReportPreviewTruncator.MakePreview(raw, maxChars, maxParas);

            var previewDto = ToDto(r, includeContent: false);
            previewDto.ContentText = preview;

            previewDto.AccessLevel = "PreviewOnly";
            previewDto.IsBlocked = false;

            // If decision says hard stop for reports without subscription, enforce it
            previewDto.HardStop = decision.HardStop;

            ApplyEntitlementMeta(previewDto, decision, maxChars, maxParas);

            return Ok(previewDto);
        }

        // -------------------------
        // PUBLIC COURTS LOOKUP (for filters)
        // GET /api/law-reports/courts?countryId=1&q=high
        // -------------------------
        [HttpGet("courts")]
        [AllowAnonymous]
        public async Task<ActionResult<List<object>>> PublicCourtsLookup(
            [FromQuery] int countryId,
            [FromQuery] string? q = null,
            CancellationToken ct = default)
        {
            if (countryId <= 0)
                return BadRequest(new { message = "countryId is required." });

            q = (q ?? "").Trim();

            var query = _db.Courts
                .AsNoTracking()
                .Where(c => c.CountryId == countryId && c.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(c =>
                    (c.Name != null && c.Name.Contains(q)) ||
                    (c.Code != null && c.Code.Contains(q)));
            }

            var items = await query
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    code = c.Code
                })
                .ToListAsync(ct);

            return Ok(items);
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
                    .Include(x => x.TownRef)
                    .Include(x => x.CourtRef)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(q))
                {
                    query = query.Where(r =>
                        (r.ReportNumber != null && r.ReportNumber.Contains(q)) ||
                        (r.Citation != null && r.Citation.Contains(q)) ||
                        (r.CaseNumber != null && r.CaseNumber.Contains(q)) ||
                        (r.Parties != null && r.Parties.Contains(q)) ||
                        (r.Court != null && r.Court.Contains(q)) ||
                        (r.Town != null && r.Town.Contains(q)) ||
                        (r.Judges != null && r.Judges.Contains(q)) ||
                        (r.LegalDocument != null && r.LegalDocument.Title != null && r.LegalDocument.Title.Contains(q))
                    );
                }

                var list = await query
                    .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
                    .ThenByDescending(r => r.Id)
                    .ToListAsync();

                var dtoList = list.Select(r => ToDto(r, includeContent: false)).ToList();
                return Ok(dtoList);
            }
            catch (Exception ex)
            {
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

            var courtType = (CourtType)dto.CourtType;

            // ✅ resolve town from TownId or TownPostCode/postCode alias
            (int? townId, string? townName, string? postCode) resolvedTown;
            try { resolvedTown = await ResolveTownAsync(dto); }
            catch (Exception ex) { return BadRequest(new { message = ex.Message }); }

            // ✅ resolve courtId (validate)
            int? resolvedCourtId = null;
            if (dto.CourtId.HasValue)
            {
                var court = await _db.Courts.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == dto.CourtId.Value);

                if (court == null)
                    return BadRequest(new { message = "Selected CourtId does not exist." });

                if (court.CountryId != dto.CountryId)
                    return BadRequest(new { message = "Selected court does not match the selected country." });

                resolvedCourtId = court.Id;
            }

            // ✅ Citation: auto-generate if blank (DecisionDate year)
            var ensuredCitation = await EnsureCitationAsync(dto, resolvedTown, CancellationToken.None);

            var existing = await FindExistingByDedupe(dto, resolvedTown.townId, resolvedTown.townName, ensuredCitation);
            if (existing != null)
                return Conflict(new { message = "Duplicate report exists.", existingLawReportId = existing.Id });

            var title = BuildReportTitle(dto, resolvedTown.townName, ensuredCitation);

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

            var report = new LawReport
            {
                LegalDocument = doc,

                CountryId = dto.CountryId,
                Service = dto.Service,

                TownId = resolvedTown.townId,
                Town = resolvedTown.townName,

                // ✅ NEW
                CourtId = resolvedCourtId,

                Citation = ensuredCitation,
                ReportNumber = dto.ReportNumber.Trim(),
                Year = dto.Year,
                CaseNumber = TrimOrNull(dto.CaseNumber),

                DecisionType = dto.DecisionType,
                CaseType = dto.CaseType,

                CourtType = courtType,
                CourtCategory = TrimOrNull(dto.CourtCategory),
                Court = BuildCourtDisplay(TrimOrNull(dto.Court), dto.CourtCategory, resolvedTown.townName),
                Parties = TrimOrNull(dto.Parties),
                Judges = TrimOrNull(dto.Judges),
                DecisionDate = dto.DecisionDate,
                ContentText = dto.ContentText,

                CreatedAt = DateTime.UtcNow
            };

            _db.LawReports.Add(report);
            await _db.SaveChangesAsync();

            if (report.TownId.HasValue)
                report.TownRef = await _db.Towns.AsNoTracking().FirstOrDefaultAsync(t => t.Id == report.TownId.Value);

            if (report.CourtId.HasValue)
                report.CourtRef = await _db.Courts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == report.CourtId.Value);

            return CreatedAtAction(nameof(Get), new { id = report.Id }, ToDto(report, includeContent: true));
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
                .Include(x => x.TownRef)
                .Include(x => x.CourtRef)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (r == null) return NotFound();

            (int? townId, string? townName, string? postCode) resolvedTown;
            try { resolvedTown = await ResolveTownAsync(dto); }
            catch (Exception ex) { return BadRequest(new { message = ex.Message }); }

            // ✅ resolve courtId (validate)
            int? resolvedCourtId = null;
            if (dto.CourtId.HasValue)
            {
                var court = await _db.Courts.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == dto.CourtId.Value);

                if (court == null)
                    return BadRequest(new { message = "Selected CourtId does not exist." });

                if (court.CountryId != dto.CountryId)
                    return BadRequest(new { message = "Selected court does not match the selected country." });

                resolvedCourtId = court.Id;
            }

            var ensuredCitation = await EnsureCitationAsync(dto, resolvedTown, CancellationToken.None);

            var existing = await FindExistingByDedupe(dto, resolvedTown.townId, resolvedTown.townName, ensuredCitation);
            if (existing != null && existing.Id != id)
                return Conflict(new { message = "Duplicate report exists.", existingLawReportId = existing.Id });

            r.CountryId = dto.CountryId;
            r.Service = dto.Service;

            r.Citation = ensuredCitation;
            r.ReportNumber = dto.ReportNumber.Trim();
            r.Year = dto.Year;
            r.CaseNumber = TrimOrNull(dto.CaseNumber);

            r.DecisionType = dto.DecisionType;
            r.CaseType = dto.CaseType;

            r.CourtType = (CourtType)dto.CourtType;
            r.CourtCategory = TrimOrNull(dto.CourtCategory);
            // keep Court string in sync (legacy/display)
            r.Court = BuildCourtDisplay(TrimOrNull(dto.Court), dto.CourtCategory, resolvedTown.townName);


            // ✅ town
            r.TownId = resolvedTown.townId;
            r.Town = resolvedTown.townName;

            // ✅ NEW: court FK persisted
            r.CourtId = resolvedCourtId;

            r.Parties = TrimOrNull(dto.Parties);
            r.Judges = TrimOrNull(dto.Judges);
            r.DecisionDate = dto.DecisionDate;
            r.ContentText = dto.ContentText;

            r.UpdatedAt = DateTime.UtcNow;

            if (r.LegalDocument != null)
            {
                r.LegalDocument.Title = BuildReportTitle(dto, resolvedTown.townName, ensuredCitation);
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
        // UPDATE CONTENT ONLY (Admin Report Content screen)
        // -------------------------
        [Authorize(Roles = "Admin")]
        [HttpPut("{id:int}/content")]
        public async Task<IActionResult> UpdateContent(int id, [FromBody] LawReportContentUpdateDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest(new { message = "Payload is required." });
            if (string.IsNullOrWhiteSpace(dto.ContentText))
                return BadRequest(new { message = "ContentText is required." });

            var r = await _db.LawReports
                .Include(x => x.LegalDocument)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (r == null) return NotFound();

            var normalized = LawAfrica.API.Services.Html.ReportHtmlNormalizer.Normalize(dto.ContentText);
            r.ContentText = LawAfrica.API.Services.Html.ReportHtmlSanitizer.Sanitize(normalized);
            r.UpdatedAt = DateTime.UtcNow;

            if (dto.DecisionType.HasValue) r.DecisionType = dto.DecisionType.Value;
            if (dto.CaseType.HasValue) r.CaseType = dto.CaseType.Value;

            await _db.SaveChangesAsync(ct);
            return NoContent();
        }
        // GET /api/law-reports/case-types
        [HttpGet("case-types")]
        [AllowAnonymous]
        public async Task<IActionResult> GetCaseTypes(CancellationToken ct)
        {
            // grouped counts of CaseType used in current reports
            var groups = await _db.LawReports
                .AsNoTracking()
                .Where(r => r.CaseType != null)
                .GroupBy(r => r.CaseType)
                .Select(g => new { value = (int)g.Key, count = g.Count() })
                .ToListAsync(ct);

            var items = groups
                .OrderBy(x => CaseTypeLabel((ReportCaseType)x.value))
                .Select(x => new
                {
                    value = x.value,
                    label = CaseTypeLabel((ReportCaseType)x.value),
                    count = x.count
                })
                .ToList();

            return Ok(items);
        }
        // GET /api/law-reports/decision-types
        [HttpGet("decision-types")]
        [AllowAnonymous]
        public async Task<IActionResult> GetDecisionTypes(CancellationToken ct)
        {
            var groups = await _db.LawReports
                .AsNoTracking()
                .Where(r => r.DecisionType != null)
                .GroupBy(r => r.DecisionType)
                .Select(g => new { value = (int)g.Key, count = g.Count() })
                .ToListAsync(ct);

            var items = groups
                .OrderBy(x => DecisionTypeLabel((ReportDecisionType)x.value))
                .Select(x => new
                {
                    value = x.value,
                    label = DecisionTypeLabel((ReportDecisionType)x.value),
                    count = x.count
                })
                .ToList();

            return Ok(items);
        }

        public class LawReportSearchResponse
        {
            public int Total { get; set; }
            public List<object> Items { get; set; } = new();
        }

        // GET /api/law-reports/search?... (matches your frontend)
        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<IActionResult> Search(
            [FromQuery] string? q = null,
            [FromQuery] string? reportNumber = null,
            [FromQuery] string? parties = null,
            [FromQuery] string? citation = null,
            [FromQuery] int? year = null,
            [FromQuery] int? courtType = null,
            [FromQuery] int? courtId = null,
            [FromQuery] string? townOrPostCode = null,
            [FromQuery] int? caseType = null,
            [FromQuery] int? decisionType = null,
            [FromQuery] string? sort = "year_desc",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 9,
            [FromQuery] int? countryId = null,
            [FromQuery] int? townId = null,
            CancellationToken ct = default)
        {
            q = (q ?? "").Trim();
            reportNumber = (reportNumber ?? "").Trim();
            parties = (parties ?? "").Trim();
            citation = (citation ?? "").Trim();
            townOrPostCode = (townOrPostCode ?? "").Trim();

            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 9;
            if (pageSize > 60) pageSize = 60;

            var query = _db.LawReports
                .AsNoTracking()
                .Include(r => r.LegalDocument)
                .Include(r => r.TownRef)
                .Include(r => r.CourtRef)
                .Where(r =>
                    r.LegalDocument != null &&
                    r.LegalDocument.Status == LegalDocumentStatus.Published &&
                    r.LegalDocument.Kind == LegalDocumentKind.Report &&
                    r.LegalDocument.FileType == "report");

            if (countryId.HasValue && countryId.Value > 0)
                query = query.Where(r => r.CountryId == countryId.Value);

            if (year.HasValue) query = query.Where(r => r.Year == year.Value);

            if (courtType.HasValue && courtType.Value > 0)
                query = query.Where(r => r.CourtType.HasValue && (int)r.CourtType.Value == courtType.Value);

            if (courtId.HasValue && courtId.Value > 0)
                query = query.Where(r => r.CourtId == courtId.Value);
            if (townId.HasValue && townId.Value > 0)
                query = query.Where(r => r.TownId == townId.Value);

            if (caseType.HasValue && caseType.Value > 0)
                query = query.Where(r => (int)r.CaseType == caseType.Value);

            if (decisionType.HasValue && decisionType.Value > 0)
                query = query.Where(r => (int)r.DecisionType == decisionType.Value);

            if (!string.IsNullOrWhiteSpace(reportNumber))
                query = query.Where(r => r.ReportNumber != null && r.ReportNumber.Contains(reportNumber));

            if (!string.IsNullOrWhiteSpace(parties))
                query = query.Where(r => r.Parties != null && r.Parties.Contains(parties));

            if (!string.IsNullOrWhiteSpace(citation))
                query = query.Where(r => r.Citation != null && r.Citation.Contains(citation));

            if (!string.IsNullOrWhiteSpace(townOrPostCode))
            {
                query = query.Where(r =>
                    (r.Town != null && r.Town.Contains(townOrPostCode)) ||
                    (r.TownRef != null && r.TownRef.PostCode != null && r.TownRef.PostCode.Contains(townOrPostCode)));
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(r =>
                    (r.LegalDocument != null && r.LegalDocument.Title != null && r.LegalDocument.Title.Contains(q)) ||
                    (r.Parties != null && r.Parties.Contains(q)) ||
                    (r.ReportNumber != null && r.ReportNumber.Contains(q)) ||
                    (r.Citation != null && r.Citation.Contains(q)) ||
                    (r.CaseNumber != null && r.CaseNumber.Contains(q)) ||
                    (r.CourtRef != null && r.CourtRef.Name != null && r.CourtRef.Name.Contains(q)) ||
                    (r.Court != null && r.Court.Contains(q)) ||
                    (r.Town != null && r.Town.Contains(q)) ||
                    (r.Judges != null && r.Judges.Contains(q))
                );
            }

            var total = await query.CountAsync(ct);

            // Sorting
            query = sort switch
            {
                "year_asc" => query.OrderBy(r => r.Year).ThenBy(r => r.Id),
                "reportno_asc" => query.OrderBy(r => r.ReportNumber).ThenBy(r => r.Id),
                "parties_asc" => query.OrderBy(r => r.Parties).ThenBy(r => r.Id),
                "date_desc" => query.OrderByDescending(r => r.DecisionDate).ThenByDescending(r => r.Id),
                _ => query.OrderByDescending(r => r.Year).ThenByDescending(r => r.Id), // year_desc
            };

            var skip = (page - 1) * pageSize;

            var list = await query
                .Skip(skip)
                .Take(pageSize)
                .Select(r => new
                {
                    id = r.Id,
                    legalDocumentId = r.LegalDocumentId,
                    isPremium = r.LegalDocument != null && r.LegalDocument.IsPremium,

                    // ✅ IMPORTANT: Title comes from LegalDocument.Title (your Create/Update already sets it)
                    title = r.LegalDocument != null ? (r.LegalDocument.Title ?? "") : "",

                    parties = r.Parties ?? "",
                    reportNumber = r.ReportNumber ?? "",
                    citation = r.Citation ?? "",
                    year = r.Year,

                    decisionDate = r.DecisionDate,

                    caseTypeLabel = CaseTypeLabel(r.CaseType),
                    decisionTypeLabel = DecisionTypeLabel(r.DecisionType),

                    courtTypeLabel = r.CourtType.HasValue ? CourtTypeLabel(r.CourtType.Value) : "",

                    courtName = r.CourtRef != null ? r.CourtRef.Name : null,
                    town = r.Town ?? "",
                    townPostCode = r.TownRef != null ? r.TownRef.PostCode : null,
                    judges = r.Judges ?? "",

                    // simple preview (you can improve later)
                    previewText = (r.ContentText ?? "").Length > 650 ? (r.ContentText ?? "").Substring(0, 650) : (r.ContentText ?? "")
                })
                .ToListAsync(ct);

            return Ok(new { items = list, total });
        }



        public class LawReportContentUpdateDto
        {
            public string ContentText { get; set; } = "";
            public ReportDecisionType? DecisionType { get; set; }
            public ReportCaseType? CaseType { get; set; }
        }



        // ============================================================
        // DTO mapping
        // ============================================================
        private LawReportDto ToDto(LawReport r, bool includeContent)
        {
            return new LawReportDto
            {
                Id = r.Id,
                LegalDocumentId = r.LegalDocumentId,
                IsPremium = r.LegalDocument != null && r.LegalDocument.IsPremium,

                CountryId = r.CountryId,
                Service = r.Service,

                // ✅ nullable-safe
                CourtType = r.CourtType.HasValue ? (int)r.CourtType.Value : (int?)null,

                // ✅ NEW: court FK + metadata for UI
                CourtId = r.CourtId,
                CourtName = r.CourtRef != null ? r.CourtRef.Name : null,
                CourtCode = r.CourtRef != null ? r.CourtRef.Code : null,

                ReportNumber = r.ReportNumber,
                Year = r.Year,
                CaseNumber = r.CaseNumber,
                Citation = r.Citation,

                DecisionType = r.DecisionType,
                CaseType = r.CaseType,

                Court = r.Court,
                CourtCategory = r.CourtCategory,
                Town = r.Town,
                TownId = r.TownId,
                TownPostCode = r.TownRef != null ? r.TownRef.PostCode : null,

                Parties = r.Parties,
                Judges = r.Judges,
                DecisionDate = r.DecisionDate,

                ContentText = includeContent ? (r.ContentText ?? "") : "",

                Title = r.LegalDocument?.Title ?? "",

                ServiceLabel = ServiceLabel(r.Service),
                CourtTypeLabel = CourtTypeLabel(r.CourtType),
                DecisionTypeLabel = DecisionTypeLabel(r.DecisionType),
                CaseTypeLabel = CaseTypeLabel(r.CaseType),

                IsBlocked = false,
                BlockReason = null,
                BlockMessage = null,

                HardStop = false,
                AccessLevel = includeContent ? "FullAccess" : "PreviewOnly",

                RequiredProductId = null,
                RequiredProductName = null,
                RequiredAction = "None",

                CtaLabel = null,
                CtaUrl = null,
                SecondaryCtaLabel = null,
                SecondaryCtaUrl = null,

                PreviewMaxChars = null,
                PreviewMaxParagraphs = null,

                FromCache = false,
                GrantSource = null,
                DebugNote = null
            };
        }

        // ============================================================
        // helpers (Town resolver + dedupe + citation)
        // ============================================================
        private static string? TrimOrNull(string? s)
        {
            var t = (s ?? "").Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        private async Task<(int? townId, string? townName, string? postCode)> ResolveTownAsync(LawReportUpsertDto dto)
        {
            if (dto.TownId.HasValue)
            {
                if (dto.TownId.Value <= 0)
                    throw new InvalidOperationException("TownId must be a positive number.");

                var t = await _db.Towns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.TownId.Value);
                if (t == null)
                    throw new InvalidOperationException("Selected TownId does not exist.");

                if (t.CountryId != dto.CountryId)
                    throw new InvalidOperationException("Selected town does not match the selected country.");

                return (t.Id, t.Name, t.PostCode);
            }

            // ✅ accept both TownPostCode (preferred) and postCode alias
            var pc = TrimOrNull(dto.TownPostCode) ?? TrimOrNull(dto.PostCode);
            if (!string.IsNullOrWhiteSpace(pc))
            {
                var t = await _db.Towns.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.CountryId == dto.CountryId && x.PostCode == pc);

                if (t == null)
                    throw new InvalidOperationException($"Town not found for PostCode '{pc}' in the selected country.");

                return (t.Id, t.Name, t.PostCode);
            }

            var townText = TrimOrNull(dto.Town);
            return (null, townText, null);
        }

        private async Task<LawReport?> FindExistingByDedupe(
            LawReportUpsertDto dto,
            int? resolvedTownId,
            string? resolvedTownName,
            string? ensuredCitation,
            CancellationToken ct = default)
        {
            var citation = TrimOrNull(ensuredCitation ?? dto.Citation);
            var reportNumber = dto.ReportNumber.Trim();
            var caseNo = TrimOrNull(dto.CaseNumber);
            var courtType = (CourtType)dto.CourtType;

            if (!string.IsNullOrWhiteSpace(citation))
            {
                var byCitation = await _db.LawReports.AsNoTracking().FirstOrDefaultAsync(x => x.Citation == citation, ct);
                if (byCitation != null) return byCitation;
            }

            if (resolvedTownId.HasValue)
            {
                return await _db.LawReports.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.ReportNumber == reportNumber &&
                    x.Year == dto.Year &&
                    x.CaseNumber == caseNo &&
                    x.CourtType == courtType &&
                    x.TownId == resolvedTownId.Value
                , ct);
            }

            var town = resolvedTownName;
            return await _db.LawReports.AsNoTracking().FirstOrDefaultAsync(x =>
                x.ReportNumber == reportNumber &&
                x.Year == dto.Year &&
                x.CaseNumber == caseNo &&
                x.CourtType == courtType &&
                x.TownId == null &&
                x.Town == town
            , ct);
        }

        private async Task<string?> EnsureCitationAsync(
            LawReportUpsertDto dto,
            (int? townId, string? townName, string? postCode) resolvedTown,
            CancellationToken ct)
        {
            var existing = TrimOrNull(dto.Citation);
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;

            if (dto.DecisionDate == null)
                throw new InvalidOperationException("DecisionDate is required to auto-generate Citation year. Provide Citation or DecisionDate.");

            var citationYear = dto.DecisionDate.Value.Year;

            var series = ServiceShortCode(dto.Service);
            var court = CourtShortCode((CourtType)dto.CourtType);
            var townCode = TownShortCode(resolvedTown.townName);
            var tail = !string.IsNullOrWhiteSpace(dto.CaseNumber) ? dto.CaseNumber!.Trim() : $"{dto.ReportNumber.Trim()}/{dto.Year}";

            var baseCitation = $"[{citationYear}] {series} ({court}{(string.IsNullOrWhiteSpace(townCode) ? "" : "-" + townCode)}) {tail}".Trim();

            var candidate = baseCitation;
            int n = 2;

            while (await _db.LawReports.AsNoTracking().AnyAsync(x => x.Citation == candidate, ct))
            {
                candidate = $"{baseCitation}-{n}";
                n++;
                if (n > 999) throw new InvalidOperationException("Unable to generate unique Citation (too many collisions).");
            }

            return candidate;
        }

        private static string BuildReportTitle(LawReportUpsertDto dto, string? resolvedTownName, string? ensuredCitation)
        {
            var parts = new List<string> { $"{dto.ReportNumber.Trim()} ({dto.Year})" };

            if (!string.IsNullOrWhiteSpace(dto.Parties))
                parts.Add(dto.Parties.Trim());

            var citation = TrimOrNull(ensuredCitation ?? dto.Citation);
            if (!string.IsNullOrWhiteSpace(citation))
                parts.Add(citation);

            var courtLabel = CourtTypeLabel((CourtType)dto.CourtType);
            var town = TrimOrNull(resolvedTownName);

            if (!string.IsNullOrWhiteSpace(courtLabel))
            {
                var courtWithTown = !string.IsNullOrWhiteSpace(town)
                    ? $"{courtLabel} — {town}"
                    : courtLabel;

                parts.Add(courtWithTown);
            }

            return string.Join(" - ", parts);
        }

        // ============================================================
        // Labels + short codes
        // ============================================================
        private static string ServiceShortCode(ReportService service) => service switch
        {
            ReportService.LawAfricaLawReports_LLR => "LLR",
            ReportService.EastAfricaLawReports_EALR => "EALR",
            ReportService.UgandaLawReports_ULR => "ULR",
            ReportService.TanzaniaLawReports_TLR => "TLR",
            ReportService.SouthernSudanLawReportsAndJournal_SSLRJ => "SSLRJ",
            ReportService.EastAfricaCourtOfAppealReports_EACA => "EACA",
            ReportService.EastAfricaGeneralReports_EAGR => "EAGR",
            ReportService.EastAfricaProtectorateLawReports_EAPLR => "EAPLR",
            ReportService.ZanzibarProtectorateLawReports_ZPLR => "ZPLR",
            ReportService.OdungasDigest => "OD",
            ReportService.CompanyRegistrySearch => "CRS",
            ReportService.UgandaLawSocietyReports_ULSR => "ULSR",
            ReportService.KenyaIndustrialPropertyInstitute => "KIPI",
            _ => "LLR"
        };

        private static string CourtShortCode(CourtType ct) => ct switch
        {
            CourtType.SupremeCourt => "SC",
            CourtType.CourtOfAppeal => "CA",
            CourtType.HighCourt => "HC",
            CourtType.EmploymentAndLabourRelationsCourt => "ELRC",
            CourtType.EnvironmentAndLandCourt => "ELC",
            CourtType.MagistratesCourts => "MC",
            CourtType.KadhisCourts => "KHC",
            CourtType.CourtsMartial => "CM",
            CourtType.SmallClaimsCourt => "SCC",
            CourtType.Tribunals => "TRIB",
            _ => "HC"
        };

        private static string? TownShortCode(string? townName)
        {
            var t = TrimOrNull(townName);
            if (string.IsNullOrWhiteSpace(t)) return null;
            var c = t.Trim()[0];
            return char.IsLetter(c) ? char.ToUpperInvariant(c).ToString() : null;
        }

        private static string ServiceLabel(ReportService service) => service switch
        {
            ReportService.LawAfricaLawReports_LLR => "LawAfrica Law Reports (LLR)",
            ReportService.OdungasDigest => "Odungas Digest",
            ReportService.UgandaLawReports_ULR => "Uganda Law Reports (ULR)",
            ReportService.TanzaniaLawReports_TLR => "Tanzania Law Reports (TLR)",
            ReportService.SouthernSudanLawReportsAndJournal_SSLRJ => "Southern Sudan Law Reports & Journal (SSLRJ)",
            ReportService.EastAfricaLawReports_EALR => "East Africa Law Reports (EALR)",
            ReportService.EastAfricaCourtOfAppealReports_EACA => "East Africa Court of Appeal Reports (EACA)",
            ReportService.EastAfricaGeneralReports_EAGR => "East Africa General Reports (EAGR)",
            ReportService.EastAfricaProtectorateLawReports_EAPLR => "East Africa Protectorate Law Reports (EAPLR)",
            ReportService.ZanzibarProtectorateLawReports_ZPLR => "Zanzibar Protectorate Law Reports (ZPLR)",
            ReportService.CompanyRegistrySearch => "Company Registry Search",
            ReportService.UgandaLawSocietyReports_ULSR => "Uganda Law Society Reports (ULSR)",
            ReportService.KenyaIndustrialPropertyInstitute => "Kenya Industrial Property Institute",
            _ => "—"
        };

        private static string CourtTypeLabel(CourtType? ct) => ct switch
        {
            CourtType.SupremeCourt => "Supreme Court",
            CourtType.CourtOfAppeal => "Court of Appeal",
            CourtType.HighCourt => "High Court",
            CourtType.EmploymentAndLabourRelationsCourt => "Employment & Labour Relations Court",
            CourtType.EnvironmentAndLandCourt => "Environment & Land Court",
            CourtType.MagistratesCourts => "Magistrates Courts",
            CourtType.KadhisCourts => "Kadhi's Courts",
            CourtType.CourtsMartial => "Courts Martial",
            CourtType.SmallClaimsCourt => "Small Claims Court",
            CourtType.Tribunals => "Tribunals",
            _ => "—"
        };

        private static string DecisionTypeLabel(ReportDecisionType v) => v switch
        {
            ReportDecisionType.Judgment => "Judgment",
            ReportDecisionType.Ruling => "Ruling",
            ReportDecisionType.Award => "Award",
            ReportDecisionType.AwardByConsent => "Award by Consent",
            ReportDecisionType.NoticeofMotion => "Notice of Motion",
            ReportDecisionType.InterpretationofAwrd => "Interpretation of Award",
            ReportDecisionType.Order => "Order",
            ReportDecisionType.InterpretationofAmendedOrder => "Interpretation of Amended Order",
            _ => "—"
        };

        private static string CaseTypeLabel(ReportCaseType v) => v switch
        {
            ReportCaseType.Criminal => "Criminal",
            ReportCaseType.Civil => "Civil",
            ReportCaseType.Environmental => "Environmental",
            ReportCaseType.Family => "Family",
            ReportCaseType.Commercial => "Commercial",
            ReportCaseType.Constitutional => "Constitutional",
            _ => "—"
        };

        // ============================================================
        // Small helpers for expressions + preview limits + meta
        // ============================================================
        private static Expression<Func<LawReport, bool>> OrElse(
            Expression<Func<LawReport, bool>> left,
            Expression<Func<LawReport, bool>> right)
        {
            var p = Expression.Parameter(typeof(LawReport), "r");

            var leftBody = Expression.Invoke(left, p);
            var rightBody = Expression.Invoke(right, p);

            return Expression.Lambda<Func<LawReport, bool>>(
                Expression.OrElse(leftBody, rightBody),
                p
            );
        }

        private (int maxChars, int maxParagraphs) GetPreviewLimits(DocumentEntitlementDecision decision)
        {
            var maxChars = decision.PreviewMaxChars ?? 2200;
            var maxParas = decision.PreviewMaxParagraphs ?? 6;

            if (maxChars < 200) maxChars = 200;
            if (maxChars > 50_000) maxChars = 50_000;

            if (maxParas < 1) maxParas = 1;
            if (maxParas > 200) maxParas = 200;

            return (maxChars, maxParas);
        }

        private void ApplyEntitlementMeta(
            LawReportDto dto,
            DocumentEntitlementDecision decision,
            int? previewMaxChars,
            int? previewMaxParagraphs)
        {
            dto.RequiredProductId = decision.RequiredProductId;
            dto.RequiredProductName = decision.RequiredProductName;

            dto.RequiredAction = decision.RequiredAction switch
            {
                EntitlementRequiredAction.Subscribe => "Subscribe",
                EntitlementRequiredAction.Buy => "Buy",
                _ => "None"
            };

            dto.CtaLabel = decision.CtaLabel;
            dto.CtaUrl = decision.CtaUrl;
            dto.SecondaryCtaLabel = decision.SecondaryCtaLabel;
            dto.SecondaryCtaUrl = decision.SecondaryCtaUrl;

            dto.PreviewMaxChars = previewMaxChars ?? decision.PreviewMaxChars;
            dto.PreviewMaxParagraphs = previewMaxParagraphs ?? decision.PreviewMaxParagraphs;

            dto.HardStop = decision.HardStop;

            dto.GrantSource = decision.GrantSource.ToString();
            dto.DebugNote = decision.DebugNote;
        }

            private static string BuildCourtDisplay(string? baseCourt, string? courtCategory, string? town)
            {
                var c = TrimOrNull(baseCourt);
                var cat = TrimOrNull(courtCategory);
                var t = TrimOrNull(town);

                var head = c;

                if (!string.IsNullOrWhiteSpace(cat))
                    head = !string.IsNullOrWhiteSpace(head) ? $"{head} — {cat}" : cat;

                if (!string.IsNullOrWhiteSpace(head) && !string.IsNullOrWhiteSpace(t))
                    return $"{head} at {t}";

                return head ?? t ?? "";
            }

    }
}
