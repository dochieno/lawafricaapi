using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Reports;
using LawAfrica.API.Models;
using LawAfrica.API.Models.LawReports.Enums;
using LawAfrica.API.Models.Reports;
using LawAfrica.API.Models.Locations;
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
                .Include(x => x.TownRef)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (r == null) return NotFound();
            return Ok(ToDto(r, includeContent: true));
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

            (int? townId, string? townName, string? postCode) resolvedTown;
            try { resolvedTown = await ResolveTownAsync(dto); }
            catch (Exception ex) { return BadRequest(new { message = ex.Message }); }

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

                Citation = ensuredCitation,
                ReportNumber = dto.ReportNumber.Trim(),
                Year = dto.Year,
                CaseNumber = TrimOrNull(dto.CaseNumber),

                DecisionType = dto.DecisionType,
                CaseType = dto.CaseType,

                CourtType = courtType,
                Court = TrimOrNull(dto.Court),

                Parties = TrimOrNull(dto.Parties),
                Judges = TrimOrNull(dto.Judges),
                DecisionDate = dto.DecisionDate,
                ContentText = dto.ContentText,

                CreatedAt = DateTime.UtcNow
            };

            _db.LawReports.Add(report);
            await _db.SaveChangesAsync();

            // Attach TownRef for DTO convenience
            if (report.TownId.HasValue)
                report.TownRef = await _db.Towns.AsNoTracking().FirstOrDefaultAsync(t => t.Id == report.TownId.Value);

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
                .FirstOrDefaultAsync(x => x.Id == id);

            if (r == null) return NotFound();

            (int? townId, string? townName, string? postCode) resolvedTown;
            try { resolvedTown = await ResolveTownAsync(dto); }
            catch (Exception ex) { return BadRequest(new { message = ex.Message }); }

            // ✅ If citation is blank, auto-generate; otherwise keep user-provided
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
            r.Court = TrimOrNull(dto.Court);

            r.TownId = resolvedTown.townId;
            r.Town = resolvedTown.townName;

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

            // Only ContentText is required here
            if (string.IsNullOrWhiteSpace(dto.ContentText))
                return BadRequest(new { message = "ContentText is required." });

            var r = await _db.LawReports
                .Include(x => x.LegalDocument)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (r == null) return NotFound();

            r.ContentText = dto.ContentText;
            r.UpdatedAt = DateTime.UtcNow;

            // Optional: allow updating these from content page if you want
            if (dto.DecisionType.HasValue) r.DecisionType = dto.DecisionType.Value;
            if (dto.CaseType.HasValue) r.CaseType = dto.CaseType.Value;

            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        // Minimal DTO for content updates
        public class LawReportContentUpdateDto
        {
            public string ContentText { get; set; } = "";

            // optional – keep/remove depending on your content screen
            public ReportDecisionType? DecisionType { get; set; }
            public ReportCaseType? CaseType { get; set; }
        }

        // ============================================================
        // ✅ NEW: BULK IMPORT ENDPOINT (FAST)
        // POST /api/law-reports/import
        // Body: { items: [...], batchSize?: 200, stopOnError?: false, dryRun?: false }
        // ============================================================
        [Authorize(Roles = "Admin")]
        [HttpPost("import")]
        public async Task<ActionResult<LawReportBulkImportResponse>> Import([FromBody] LawReportBulkImportRequest req, CancellationToken ct)
        {
            req ??= new LawReportBulkImportRequest();

            var items = req.Items ?? new List<LawReportUpsertDto>();
            if (items.Count == 0)
                return BadRequest(new { message = "items is required and cannot be empty." });

            var batchSize = Math.Clamp(req.BatchSize ?? 200, 1, 500);
            var stopOnError = req.StopOnError ?? false;
            var dryRun = req.DryRun ?? false;

            var response = new LawReportBulkImportResponse
            {
                Total = items.Count,
                BatchSize = batchSize,
                DryRun = dryRun,
                StopOnError = stopOnError
            };

            // Track citations used in this import to prevent same-batch collisions
            var usedCitations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int offset = 0; offset < items.Count; offset += batchSize)
            {
                var batch = items.Skip(offset).Take(batchSize).ToList();

                // ✅ Resolve towns in bulk for this batch (TownId + TownPostCode)
                var resolvedTownMap = await ResolveTownsForBatchAsync(batch, ct);

                // Build entities for valid rows (in-memory), but still return per-row result
                var pendingReports = new List<LawReport>();
                var rowBindings = new List<(int index, LawReport report, LegalDocument doc, string? citation)>();

                for (int i = 0; i < batch.Count; i++)
                {
                    var globalIndex = offset + i;
                    var dto = batch[i];

                    var result = new LawReportImportItemResult
                    {
                        Index = globalIndex
                    };

                    // Basic required fields validation (same intent as UI)
                    var validationError = ValidateForImport(dto);
                    if (!string.IsNullOrWhiteSpace(validationError))
                    {
                        result.Status = "failed";
                        result.Message = validationError;
                        response.Items.Add(result);
                        response.Failed++;
                        if (stopOnError) return Ok(response);
                        continue;
                    }

                    // Town resolution result
                    var rt = resolvedTownMap.TryGetValue(i, out var tv)
                        ? tv
                        : (townId: (int?)null, townName: TrimOrNull(dto.Town), postCode: TrimOrNull(dto.TownPostCode));

                    try
                    {
                        // Ensure citation (blank => generate)
                        var ensuredCitation = await EnsureCitationAsync(dto, rt, ct);

                        // Enforce same-batch uniqueness
                        if (!string.IsNullOrWhiteSpace(ensuredCitation))
                        {
                            ensuredCitation = EnsureUniqueWithinBatch(ensuredCitation, usedCitations);
                            usedCitations.Add(ensuredCitation);
                        }

                        // Duplicate check (fast): citation-first, else composite check
                        var dup = await FindExistingByDedupe(dto, rt.townId, rt.townName, ensuredCitation, ct);
                        if (dup != null)
                        {
                            result.Status = "duplicate";
                            result.Message = "Duplicate report exists.";
                            result.ExistingLawReportId = dup.Id;
                            response.Items.Add(result);
                            response.Duplicates++;
                            continue;
                        }

                        // Build doc + report (linked) for one SaveChanges
                        var title = BuildReportTitle(dto, rt.townName, ensuredCitation);

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

                            TownId = rt.townId,
                            Town = rt.townName,

                            Citation = ensuredCitation,
                            ReportNumber = dto.ReportNumber.Trim(),
                            Year = dto.Year,
                            CaseNumber = TrimOrNull(dto.CaseNumber),

                            DecisionType = dto.DecisionType,
                            CaseType = dto.CaseType,

                            CourtType = (CourtType)dto.CourtType,
                            Court = TrimOrNull(dto.Court),

                            Parties = TrimOrNull(dto.Parties),
                            Judges = TrimOrNull(dto.Judges),
                            DecisionDate = dto.DecisionDate,
                            ContentText = dto.ContentText,

                            CreatedAt = DateTime.UtcNow
                        };

                        pendingReports.Add(report);
                        rowBindings.Add((globalIndex, report, doc, ensuredCitation));

                        // Mark “planned” created (final IDs after SaveChanges)
                        result.Status = dryRun ? "ready" : "pending";
                        result.Message = dryRun ? "Validated and ready (dry-run)." : "Pending insert.";
                        result.Citation = ensuredCitation;
                        response.Items.Add(result);
                    }
                    catch (Exception ex)
                    {
                        result.Status = "failed";
                        result.Message = ex.Message;
                        response.Items.Add(result);
                        response.Failed++;
                        if (stopOnError) return Ok(response);
                    }
                }

                if (dryRun) continue;
                if (pendingReports.Count == 0) continue;

                // ✅ Insert batch (1 SaveChanges)
                try
                {
                    _db.LawReports.AddRange(pendingReports);
                    await _db.SaveChangesAsync(ct);

                    // Fill success IDs into response items
                    // We find the response item by Index
                    var itemByIndex = response.Items
                        .Where(x => x.Status == "pending" && x.LawReportId == null)
                        .ToDictionary(x => x.Index, x => x);

                    foreach (var b in rowBindings)
                    {
                        if (itemByIndex.TryGetValue(b.index, out var it))
                        {
                            it.Status = "created";
                            it.Message = "Created.";
                            it.LawReportId = b.report.Id;
                            it.LegalDocumentId = b.doc.Id;
                            it.Citation = b.citation;
                            response.Created++;
                        }
                    }
                }
                catch (DbUpdateException dbEx)
                {
                    // Most common: unique index violation (Citation). Provide friendly message
                    // For speed we do NOT retry per-row here — we return failures for this batch.
                    // If you want a retry strategy, tell me and I’ll add it safely.
                    foreach (var b in rowBindings)
                    {
                        var it = response.Items.FirstOrDefault(x => x.Index == b.index);
                        if (it != null && (it.Status == "pending" || it.Status == "ready"))
                        {
                            it.Status = "failed";
                            it.Message = "Database rejected one or more rows (likely duplicate unique value such as Citation).";
                            it.Detail = dbEx.InnerException?.Message ?? dbEx.Message;
                            response.Failed++;
                        }
                    }

                    if (stopOnError) return Ok(response);
                }
            }

            return Ok(response);
        }

        // ============================================================
        // ✅ helpers (Town resolver + dedupe + citation)
        // ============================================================

        private static string? TrimOrNull(string? s)
        {
            var t = (s ?? "").Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        private static string? ValidateForImport(LawReportUpsertDto dto)
        {
            if (dto == null) return "Row is empty.";
            if (dto.CountryId <= 0) return "CountryId is required.";
            if ((int)dto.Service <= 0) return "Service is required.";
            if (string.IsNullOrWhiteSpace(dto.ReportNumber)) return "ReportNumber is required.";
            if (dto.Year < 1900 || dto.Year > 2100) return "Year must be between 1900 and 2100.";
            if (dto.CourtType <= 0) return "CourtType is required.";
            if (string.IsNullOrWhiteSpace(dto.ContentText)) return "ContentText is required.";
            return null;
        }

        /// <summary>
        /// Resolve town input without breaking old clients:
        /// priority:
        /// 1) dto.TownId
        /// 2) dto.TownPostCode (country-scoped)
        /// 3) dto.Town free text fallback
        /// </summary>
        private async Task<(int? townId, string? townName, string? postCode)> ResolveTownAsync(LawReportUpsertDto dto)
        {
            // 1) TownId
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

            // 2) TownPostCode
            var pc = TrimOrNull(dto.TownPostCode);
            if (!string.IsNullOrWhiteSpace(pc))
            {
                var t = await _db.Towns.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.CountryId == dto.CountryId && x.PostCode == pc);

                if (t == null)
                    throw new InvalidOperationException($"Town not found for PostCode '{pc}' in the selected country.");

                return (t.Id, t.Name, t.PostCode);
            }

            // 3) free text fallback
            var townText = TrimOrNull(dto.Town);
            return (null, townText, null);
        }

        /// <summary>
        /// ✅ Bulk resolver: resolves all TownIds/PostCodes in one DB hit for speed.
        /// returns map keyed by batch-local index (0..batchCount-1)
        /// </summary>
        private async Task<Dictionary<int, (int? townId, string? townName, string? postCode)>> ResolveTownsForBatchAsync(
            List<LawReportUpsertDto> batch,
            CancellationToken ct
        )
        {
            var map = new Dictionary<int, (int? townId, string? townName, string? postCode)>();

            var townIds = batch
                .Select((dto, idx) => new { dto, idx })
                .Where(x => x.dto?.TownId != null && x.dto.TownId.Value > 0)
                .Select(x => x.dto.TownId!.Value)
                .Distinct()
                .ToList();

            var pcs = batch
                .Select((dto, idx) => new { dto, idx })
                .Where(x => !string.IsNullOrWhiteSpace(x.dto?.TownPostCode) && x.dto.CountryId > 0)
                .Select(x => new { x.dto.CountryId, PostCode = x.dto.TownPostCode!.Trim() })
                .Distinct()
                .ToList();

            var townsById = new Dictionary<int, Town>();
            if (townIds.Count > 0)
            {
                var towns = await _db.Towns.AsNoTracking()
                    .Where(t => townIds.Contains(t.Id))
                    .ToListAsync(ct);
                townsById = towns.ToDictionary(t => t.Id, t => t);
            }

            // Pull superset by countries + postcodes
            var townsByCountryAndPc = new Dictionary<string, Town>(StringComparer.OrdinalIgnoreCase);
            if (pcs.Count > 0)
            {
                var countryIds = pcs.Select(x => x.CountryId).Distinct().ToList();
                var postCodes = pcs.Select(x => x.PostCode).Distinct().ToList();

                var towns = await _db.Towns.AsNoTracking()
                    .Where(t => countryIds.Contains(t.CountryId) && postCodes.Contains(t.PostCode))
                    .ToListAsync(ct);

                foreach (var t in towns)
                    townsByCountryAndPc[$"{t.CountryId}:{t.PostCode}"] = t;
            }

            for (int i = 0; i < batch.Count; i++)
            {
                var dto = batch[i];
                if (dto == null)
                {
                    map[i] = (null, null, null);
                    continue;
                }

                // TownId
                if (dto.TownId.HasValue && dto.TownId.Value > 0)
                {
                    if (townsById.TryGetValue(dto.TownId.Value, out var t))
                    {
                        if (t.CountryId != dto.CountryId)
                            throw new InvalidOperationException($"Row {i + 1}: TownId does not match CountryId.");

                        map[i] = (t.Id, t.Name, t.PostCode);
                        continue;
                    }
                    throw new InvalidOperationException($"Row {i + 1}: Selected TownId does not exist.");
                }

                // PostCode
                var pc = TrimOrNull(dto.TownPostCode);
                if (!string.IsNullOrWhiteSpace(pc))
                {
                    if (townsByCountryAndPc.TryGetValue($"{dto.CountryId}:{pc}", out var t))
                    {
                        map[i] = (t.Id, t.Name, t.PostCode);
                        continue;
                    }
                    throw new InvalidOperationException($"Row {i + 1}: Town not found for PostCode '{pc}' in the selected country.");
                }

                // Free text fallback
                map[i] = (null, TrimOrNull(dto.Town), null);
            }

            return map;
        }

        /// <summary>
        /// ✅ Dedupe:
        /// - if citation exists => strongest identity
        /// - else composite with TownId when available; otherwise Town string
        /// </summary>
        private async Task<LawReport?> FindExistingByDedupe(
            LawReportUpsertDto dto,
            int? resolvedTownId,
            string? resolvedTownName,
            string? ensuredCitation,
            CancellationToken ct = default
        )
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

        /// <summary>
        /// ✅ Citation auto-generator:
        /// - If dto.Citation provided => keep it
        /// - Else generate using DecisionDate year (your rule)
        /// - Enforces DecisionDate present when auto-generating
        /// - Ensures DB uniqueness by checking existing citations (best-effort)
        /// </summary>
        private async Task<string?> EnsureCitationAsync(
            LawReportUpsertDto dto,
            (int? townId, string? townName, string? postCode) resolvedTown,
            CancellationToken ct
        )
        {
            var existing = TrimOrNull(dto.Citation);
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;

            // You clarified: citation year must be DecisionDate year.
            if (dto.DecisionDate == null)
                throw new InvalidOperationException("DecisionDate is required to auto-generate Citation year. Provide Citation or DecisionDate.");

            var citationYear = dto.DecisionDate.Value.Year;

            // NOTE: adapt these pieces to your exact citation style.
            // I’m keeping it safe + predictable and easy to tweak.
            var series = ServiceShortCode(dto.Service);               // e.g. "LLR"
            var court = CourtShortCode((CourtType)dto.CourtType);     // e.g. "HC"
            var townCode = TownShortCode(resolvedTown.townName);      // e.g. "K" from Kakamega (optional)
            var tail = !string.IsNullOrWhiteSpace(dto.CaseNumber) ? dto.CaseNumber!.Trim() : $"{dto.ReportNumber.Trim()}/{dto.Year}";

            var baseCitation = $"[{citationYear}] {series} ({court}{(string.IsNullOrWhiteSpace(townCode) ? "" : "-" + townCode)}) {tail}";
            baseCitation = baseCitation.Trim();

            // Best-effort uniqueness against DB (fast):
            // If it exists, append -2, -3, ...
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

        private static string EnsureUniqueWithinBatch(string citation, HashSet<string> used)
        {
            if (!used.Contains(citation)) return citation;

            var baseC = citation;
            var n = 2;
            var candidate = $"{baseC}-{n}";
            while (used.Contains(candidate))
            {
                n++;
                candidate = $"{baseC}-{n}";
                if (n > 999) break;
            }
            return candidate;
        }

        /// <summary>
        /// ✅ Title includes court + town and citation if present (stable)
        /// </summary>
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

        // -------------------------
        // DTO mapping + LABELS
        // -------------------------
        private LawReportDto ToDto(LawReport r, bool includeContent)
        {
            return new LawReportDto
            {
                Id = r.Id,
                LegalDocumentId = r.LegalDocumentId,

                CountryId = r.CountryId,
                Service = r.Service,
                CourtType = (int)r.CourtType,

                ReportNumber = r.ReportNumber,
                Year = r.Year,
                CaseNumber = r.CaseNumber,
                Citation = r.Citation,

                DecisionType = r.DecisionType,
                CaseType = r.CaseType,

                Court = r.Court,

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
                CaseTypeLabel = CaseTypeLabel(r.CaseType)
            };
        }

        // ============================================================
        // Citation pieces (safe defaults; tweak to your standard)
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

        // Optional: 1–3 letter town code. You can replace with your own mapping later.
        private static string? TownShortCode(string? townName)
        {
            var t = TrimOrNull(townName);
            if (string.IsNullOrWhiteSpace(t)) return null;
            var c = t.Trim()[0];
            return char.IsLetter(c) ? char.ToUpperInvariant(c).ToString() : null;
        }

        // -------------------------
        // LABEL HELPERS (unchanged)
        // -------------------------
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

        private static string CourtTypeLabel(CourtType ct) => ct switch
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



        //Additional controllers:

        // ✅ NEW: END-USER LIST/SEARCH (Paged)  ✅ Step 4A
        // GET /api/law-reports/search?...filters...
        //
        // Returns lightweight list items + PreviewText from ContentText.
        // Does NOT return full ContentText.
        // ============================================================
        [Authorize]
        [HttpGet("search")]
        public async Task<ActionResult<LawReportSearchResponse>> Search(
            [FromQuery] string? q = null,
            [FromQuery] string? reportNumber = null,
            [FromQuery] string? parties = null,
            [FromQuery] string? citation = null,
            [FromQuery] int? year = null,
            [FromQuery] string? courtType = null,
            [FromQuery] string? townOrPostCode = null,
            [FromQuery] string? caseType = null,
            [FromQuery] string? decisionType = null, // ✅ NEW
            [FromQuery] string? sort = "year_desc",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 18,
            CancellationToken ct = default
        )
        {
            // Defensive paging
            page = page < 1 ? 1 : page;
            pageSize = Math.Clamp(pageSize, 1, 50);

            // Normalize strings
            q = (q ?? "").Trim();
            reportNumber = (reportNumber ?? "").Trim();
            parties = (parties ?? "").Trim();
            citation = (citation ?? "").Trim();
            courtType = (courtType ?? "").Trim();
            townOrPostCode = (townOrPostCode ?? "").Trim();
            decisionType = (decisionType ?? "").Trim();
            caseType = (caseType ?? "").Trim();

            // Start query: only LLRServices reports
            var query = _db.LawReports
                .AsNoTracking()
                .Include(x => x.LegalDocument)
                .Include(x => x.TownRef)
                .Where(r =>
                    r.LegalDocument != null &&
                    r.LegalDocument.Category == LLR_CATEGORY &&
                    r.LegalDocument.Kind == LegalDocumentKind.Report &&
                    r.LegalDocument.FileType == "report" &&
                    r.LegalDocument.Status == LegalDocumentStatus.Published
                )
                .AsQueryable();

            // ---------- Quick search ----------
            // ---------- Quick search (Postgres ILIKE, includes ContentText) ----------
            if (!string.IsNullOrWhiteSpace(q))
            {
                // Optional: guardrail to prevent abuse / huge scans
                if (q.Length > 120) q = q[..120];

                var term = q.Trim();
                var like = $"%{EscapeLike(term)}%";

                query = query.Where(r =>
                    // transcript content search ✅
                    EF.Functions.ILike(r.ContentText, like) ||

                    // metadata search ✅
                    (r.ReportNumber != null && EF.Functions.ILike(r.ReportNumber, like)) ||
                    (r.Citation != null && EF.Functions.ILike(r.Citation, like)) ||
                    (r.CaseNumber != null && EF.Functions.ILike(r.CaseNumber, like)) ||
                    (r.Parties != null && EF.Functions.ILike(r.Parties, like)) ||
                    (r.Court != null && EF.Functions.ILike(r.Court, like)) ||
                    (r.Town != null && EF.Functions.ILike(r.Town, like)) ||
                    (r.Judges != null && EF.Functions.ILike(r.Judges, like)) ||
                    (r.LegalDocument != null && r.LegalDocument.Title != null && EF.Functions.ILike(r.LegalDocument.Title, like)) ||
                    (r.TownRef != null && r.TownRef.PostCode != null && EF.Functions.ILike(r.TownRef.PostCode, like))
                );
            }

            // ---------- Specific filters ----------
            if (!string.IsNullOrWhiteSpace(reportNumber))
            {
                var v = reportNumber.ToLower();
                query = query.Where(r => r.ReportNumber != null && r.ReportNumber.ToLower().Contains(v));
            }

            if (!string.IsNullOrWhiteSpace(parties))
            {
                var v = parties.ToLower();
                query = query.Where(r => r.Parties != null && r.Parties.ToLower().Contains(v));
            }

            if (!string.IsNullOrWhiteSpace(citation))
            {
                var v = citation.ToLower();
                query = query.Where(r => r.Citation != null && r.Citation.ToLower().Contains(v));
            }

            if (year.HasValue)
            {
                query = query.Where(r => r.Year == year.Value);
            }

            // CourtType: prefer enum matching if client sends int/name; fallback to Court text contains
            if (!string.IsNullOrWhiteSpace(courtType))
            {
                if (TryParseCourtType(courtType, out var ctEnum))
                {
                    query = query.Where(r => r.CourtType == ctEnum);
                }
                else
                {
                    var v = courtType.ToLower();
                    query = query.Where(r =>
                        (r.Court != null && r.Court.ToLower().Contains(v)) ||
                        (r.LegalDocument != null && r.LegalDocument.Title != null && r.LegalDocument.Title.ToLower().Contains(v))
                    );
                }
            }

            // Town / PostCode
            if (!string.IsNullOrWhiteSpace(townOrPostCode))
            {
                var v = townOrPostCode.ToLower();

                query = query.Where(r =>
                    (r.Town != null && r.Town.ToLower().Contains(v)) ||
                    (r.TownRef != null && r.TownRef.PostCode != null && r.TownRef.PostCode.ToLower().Contains(v))
                );
            }

            // CaseType: prefer enum matching if client sends int/name
            if (!string.IsNullOrWhiteSpace(caseType))
            {
                if (TryParseCaseType(caseType, out var caseEnum))
                {
                    query = query.Where(r => r.CaseType == caseEnum);
                }
                else
                {
                    // If not parseable, we don't apply filter (avoid false negatives).
                    // Frontend should send int or enum name (Civil/Criminal/...).
                }
            }
            // DecisionType: prefer enum matching if client sends int/name (Judgment/Ruling/Award/...)
            if (!string.IsNullOrWhiteSpace(decisionType))
            {
                if (TryParseDecisionType(decisionType, out var decEnum))
                {
                    query = query.Where(r => r.DecisionType == decEnum);
                }
                else
                {
                    // If not parseable, do not filter (avoid false negatives)
                }
            }

            // Total count BEFORE paging
            var total = await query.CountAsync(ct);

            // ---------- Sorting ----------
            // supported:
            // year_desc, year_asc, date_desc, reportno_asc, parties_asc
            query = sort switch
            {
                "year_asc" => query.OrderBy(r => r.Year).ThenByDescending(r => r.Id),
                "date_desc" => query.OrderByDescending(r => r.DecisionDate).ThenByDescending(r => r.Id),
                "reportno_asc" => query.OrderBy(r => r.ReportNumber).ThenByDescending(r => r.Id),
                "parties_asc" => query.OrderBy(r => r.Parties).ThenByDescending(r => r.Id),
                _ => query.OrderByDescending(r => r.Year).ThenByDescending(r => r.Id), // year_desc default
            };

            // Page slice
            var skip = (page - 1) * pageSize;

            // Pull only page rows; create PreviewText from ContentText (SQL-translatable substring)
            const int PREVIEW_LEN = 520;

            var items = await query
                .Skip(skip)
                .Take(pageSize)
                .Select(r => new LawReportListItemDto
                {
                    Id = r.Id,
                    LegalDocumentId = r.LegalDocumentId,

                    Title = r.LegalDocument != null ? (r.LegalDocument.Title ?? "") : "",
                    IsPremium = r.LegalDocument != null && r.LegalDocument.IsPremium,

                    ReportNumber = r.ReportNumber,
                    Year = r.Year,
                    CaseNumber = r.CaseNumber,
                    Citation = r.Citation,

                    CourtType = (int)r.CourtType,
                    CourtTypeLabel = CourtTypeLabel(r.CourtType),

                    DecisionType = r.DecisionType,
                    DecisionTypeLabel = DecisionTypeLabel(r.DecisionType),

                    CaseType = r.CaseType,
                    CaseTypeLabel = CaseTypeLabel(r.CaseType),

                    Court = r.Court,

                    Town = r.Town,
                    TownId = r.TownId,
                    TownPostCode = r.TownRef != null ? r.TownRef.PostCode : null,

                    Parties = r.Parties,
                    Judges = r.Judges,
                    DecisionDate = r.DecisionDate,

                    // ✅ Key: preview snippet (not full content)
                    PreviewText =
                        r.ContentText == null ? "" :
                        (r.ContentText.Length <= PREVIEW_LEN ? r.ContentText : r.ContentText.Substring(0, PREVIEW_LEN))
                })
                .ToListAsync(ct);

            // Light cleanup for nicer previews (page-only; safe)
            foreach (var it in items)
            {
                it.PreviewText = CleanPreview(it.PreviewText);
            }

            return Ok(new LawReportSearchResponse
            {
                Items = items,
                Total = total,
                Page = page,
                PageSize = pageSize
            });
        }

        // ============================================================
        // ✅ NEW: Case Types (distinct from DB)  ✅ Step 4B
        // GET /api/law-reports/case-types
        // Returns only case types that exist in DB (with counts)
        // ============================================================
        [Authorize]
        [HttpGet("case-types")]
        public async Task<ActionResult<List<LawReportCaseTypeOptionDto>>> GetCaseTypes(CancellationToken ct)
        {
            // 1) Get raw distinct values + counts from SQL (no label mapping here)
            var raw = await (
                from r in _db.LawReports.AsNoTracking()
                join d in _db.LegalDocuments.AsNoTracking() on r.LegalDocumentId equals d.Id
                where d.Category == LLR_CATEGORY
                      && d.Kind == LegalDocumentKind.Report
                      && d.FileType == "report"
                      && d.Status == LegalDocumentStatus.Published
                group r by r.CaseType into g
                select new
                {
                    Value = (int)g.Key,
                    Count = g.Count()
                }
            ).ToListAsync(ct);

            // 2) Map labels in-memory (safe)
            var data = raw
                .Where(x => x.Value > 0) // ignore 0 / unknown if present
                .Select(x =>
                {
                    var enumVal = (ReportCaseType)x.Value;

                    // Safe label: never throw
                    string label;
                    try { label = CaseTypeLabel(enumVal) ?? enumVal.ToString(); }
                    catch { label = enumVal.ToString(); }

                    return new LawReportCaseTypeOptionDto
                    {
                        Value = x.Value,
                        Label = label,
                        Count = x.Count
                    };
                })
                .OrderBy(x => x.Label)
                .ToList();

            return Ok(data);
        }


        [Authorize]
        [HttpGet("decision-types")]
        public async Task<ActionResult<List<LawReportDecisionTypeOptionDto>>> GetDecisionTypes(CancellationToken ct)
        {
            var raw = await (
                from r in _db.LawReports.AsNoTracking()
                join d in _db.LegalDocuments.AsNoTracking() on r.LegalDocumentId equals d.Id
                where d.Category == LLR_CATEGORY
                      && d.Kind == LegalDocumentKind.Report
                      && d.FileType == "report"
                      && d.Status == LegalDocumentStatus.Published
                group r by r.DecisionType into g
                select new
                {
                    Value = (int)g.Key,
                    Count = g.Count()
                }
            ).ToListAsync(ct);

            var data = raw
                .Where(x => x.Value > 0)
                .Select(x =>
                {
                    var enumVal = (ReportDecisionType)x.Value;

                    string label;
                    try { label = DecisionTypeLabel(enumVal) ?? enumVal.ToString(); }
                    catch { label = enumVal.ToString(); }

                    return new LawReportDecisionTypeOptionDto
                    {
                        Value = x.Value,
                        Label = label,
                        Count = x.Count
                    };
                })
                .OrderBy(x => x.Label)
                .ToList();

            return Ok(data);
        }

        public class LawReportDecisionTypeOptionDto
        {
            public int Value { get; set; }
            public string Label { get; set; } = "";
            public int Count { get; set; }
        }



        // DTO for the dropdown
        public class LawReportCaseTypeOptionDto
        {
            public int Value { get; set; }       // enum int (1..6)
            public string Label { get; set; } = ""; // "Criminal", "Civil", ...
            public int Count { get; set; }       // helpful for UI
        }


        private static string CleanPreview(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var t = s.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

            // Collapse excessive whitespace while keeping paragraph breaks
            // (keeps two newlines, collapses 3+ into 2)
            while (t.Contains("\n\n\n")) t = t.Replace("\n\n\n", "\n\n");

            return t;
        }

        private static bool TryParseCaseType(string raw, out ReportCaseType v)
        {
            v = default;

            var t = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t)) return false;

            if (int.TryParse(t, out var n))
            {
                if (Enum.IsDefined(typeof(ReportCaseType), n))
                {
                    v = (ReportCaseType)n;
                    return true;
                }
                return false;
            }

            // Allow names: "Civil", "Criminal", ...
            return Enum.TryParse<ReportCaseType>(t, ignoreCase: true, out v);
        }
        private static bool TryParseDecisionType(string input, out ReportDecisionType value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var t = input.Trim();

            // int?
            if (int.TryParse(t, out var n) && Enum.IsDefined(typeof(ReportDecisionType), n))
            {
                value = (ReportDecisionType)n;
                return true;
            }

            // name / label (case-insensitive, ignore spaces & punctuation)
            var norm = new string(t.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

            foreach (var v in Enum.GetValues(typeof(ReportDecisionType)).Cast<ReportDecisionType>())
            {
                var nameNorm = new string(v.ToString().Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                if (nameNorm == norm)
                {
                    value = v;
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseCourtType(string raw, out CourtType v)
        {
            v = default;

            var t = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t)) return false;

            if (int.TryParse(t, out var n))
            {
                if (Enum.IsDefined(typeof(CourtType), n))
                {
                    v = (CourtType)n;
                    return true;
                }
                return false;
            }

            // Allow names: "HighCourt", "CourtOfAppeal", ...
            return Enum.TryParse<CourtType>(t, ignoreCase: true, out v);
        }

        // ============================================================
        // ✅ Bulk import DTOs (keep here or move to DTOs folder)
        // ============================================================
        public class LawReportBulkImportRequest
        {
            public List<LawReportUpsertDto>? Items { get; set; } = new();
            public int? BatchSize { get; set; } = 200;
            public bool? StopOnError { get; set; } = false;
            public bool? DryRun { get; set; } = false;
        }

        public class LawReportBulkImportResponse
        {
            public int Total { get; set; }
            public int BatchSize { get; set; }
            public bool DryRun { get; set; }
            public bool StopOnError { get; set; }

            public int Created { get; set; }
            public int Duplicates { get; set; }
            public int Failed { get; set; }

            public List<LawReportImportItemResult> Items { get; set; } = new();
        }

        public class LawReportImportItemResult
        {
            public int Index { get; set; }                  // 0-based index in submitted array
            public string Status { get; set; } = "pending"; // created | duplicate | failed | pending | ready
            public string Message { get; set; } = "";
            public string? Detail { get; set; }

            public int? LawReportId { get; set; }
            public int? LegalDocumentId { get; set; }
            public int? ExistingLawReportId { get; set; }

            public string? Citation { get; set; }
        }

        //To handle searches:

        public class LawReportSearchResponse
        {
            public List<LawReportListItemDto> Items { get; set; } = new();
            public int Total { get; set; }
            public int Page { get; set; }
            public int PageSize { get; set; }
        }

        public class LawReportListItemDto
        {
            public int Id { get; set; }
            public int LegalDocumentId { get; set; }

            public string Title { get; set; } = "";
            public bool IsPremium { get; set; }

            public string? ReportNumber { get; set; }
            public int Year { get; set; }
            public string? CaseNumber { get; set; }
            public string? Citation { get; set; }

            public int CourtType { get; set; }
            public string CourtTypeLabel { get; set; } = "";

            public ReportDecisionType DecisionType { get; set; }
            public string DecisionTypeLabel { get; set; } = "";

            public ReportCaseType CaseType { get; set; }
            public string CaseTypeLabel { get; set; } = "";

            public string? Court { get; set; }

            public string? Town { get; set; }
            public int? TownId { get; set; }
            public string? TownPostCode { get; set; }

            public string? Parties { get; set; }
            public string? Judges { get; set; }
            public DateTime? DecisionDate { get; set; }

            public string PreviewText { get; set; } = "";
        }

        private static string EscapeLike(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Escape LIKE wildcards so user input doesn't become a pattern
            return input
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_");
        }
    }
}
