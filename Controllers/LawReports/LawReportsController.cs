using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Reports;
using LawAfrica.API.Models;
using LawAfrica.API.Models.LawReports.Enums;
using LawAfrica.API.Models.Reports;
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
            try
            {
                resolvedTown = await ResolveTownAsync(dto);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            if (string.IsNullOrWhiteSpace(TrimOrNull(dto.Citation)) && dto.DecisionDate == null)
                return BadRequest(new { message = "DecisionDate is required to auto-generate citation year. Provide Citation or DecisionDate." });

            // ✅ Citation: manual OR auto-generate (unique)
            var citation = TrimOrNull(dto.Citation);
            if (string.IsNullOrWhiteSpace(citation))
            {
                citation = await GenerateUniqueCitationAsync(dto, resolvedTown.townName, excludeReportId: null);
            }

            // ✅ Dedupe now uses resolved/auto citation too
            var existing = await FindExistingByDedupe(dto, resolvedTown.townId, resolvedTown.townName, citationOverride: citation);
            if (existing != null)
                return Conflict(new { message = "Duplicate report exists.", existingLawReportId = existing.Id });

            var title = BuildReportTitle(dto, resolvedTown.townName);

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

                TownId = resolvedTown.townId,
                Town = resolvedTown.townName,

                Citation = citation, // ✅ always set (manual or auto)
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

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // ✅ If unique index hit due to race, regenerate + retry once
                report.Citation = await GenerateUniqueCitationAsync(dto, resolvedTown.townName, excludeReportId: null);
                await _db.SaveChangesAsync();
            }

            report.LegalDocument = doc;

            if (report.TownId.HasValue)
            {
                report.TownRef = await _db.Towns.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == report.TownId.Value);
            }

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
            try
            {
                resolvedTown = await ResolveTownAsync(dto);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            // ✅ Citation: manual OR auto-generate (unique)
            var citation = TrimOrNull(dto.Citation);
            if (string.IsNullOrWhiteSpace(citation))
            {
                citation = await GenerateUniqueCitationAsync(dto, resolvedTown.townName, excludeReportId: id);
            }

            var existing = await FindExistingByDedupe(dto, resolvedTown.townId, resolvedTown.townName, citationOverride: citation);
            if (existing != null && existing.Id != id)
                return Conflict(new { message = "Duplicate report exists.", existingLawReportId = existing.Id });

            r.CountryId = dto.CountryId;
            r.Service = dto.Service;

            r.Citation = citation;
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
                r.LegalDocument.Title = BuildReportTitle(dto, resolvedTown.townName);
                r.LegalDocument.Description = $"Law Report {dto.ReportNumber} ({dto.Year})";

                r.LegalDocument.Category = LLR_CATEGORY;
                r.LegalDocument.CountryId = dto.CountryId;

                r.LegalDocument.Kind = LegalDocumentKind.Report;
                r.LegalDocument.FileType = "report";
                r.LegalDocument.FilePath = r.LegalDocument.FilePath ?? "";
                r.LegalDocument.Version = string.IsNullOrWhiteSpace(r.LegalDocument.Version) ? "1" : r.LegalDocument.Version;
                r.LegalDocument.UpdatedAt = DateTime.UtcNow;
            }

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // ✅ If unique index hit due to race, regenerate + retry once
                r.Citation = await GenerateUniqueCitationAsync(dto, resolvedTown.townName, excludeReportId: id);
                await _db.SaveChangesAsync();
            }

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

            var pc = TrimOrNull(dto.TownPostCode);
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

        /// <summary>
        /// ✅ Updated: dedupe can use TownId if present; otherwise uses Town string.
        /// ✅ Now also accepts citationOverride (generated citation).
        /// </summary>
        private async Task<LawReport?> FindExistingByDedupe(
            LawReportUpsertDto dto,
            int? resolvedTownId,
            string? resolvedTownName,
            string? citationOverride = null
        )
        {
            var citation = TrimOrNull(citationOverride) ?? TrimOrNull(dto.Citation);
            var reportNumber = dto.ReportNumber.Trim();
            var caseNo = TrimOrNull(dto.CaseNumber);
            var courtType = (CourtType)dto.CourtType;

            if (!string.IsNullOrWhiteSpace(citation))
            {
                var byCitation = await _db.LawReports.FirstOrDefaultAsync(x => x.Citation == citation);
                if (byCitation != null) return byCitation;
            }

            if (resolvedTownId.HasValue)
            {
                return await _db.LawReports.FirstOrDefaultAsync(x =>
                    x.ReportNumber == reportNumber &&
                    x.Year == dto.Year &&
                    x.CaseNumber == caseNo &&
                    x.CourtType == courtType &&
                    x.TownId == resolvedTownId.Value
                );
            }

            var town = resolvedTownName;
            return await _db.LawReports.FirstOrDefaultAsync(x =>
                x.ReportNumber == reportNumber &&
                x.Year == dto.Year &&
                x.CaseNumber == caseNo &&
                x.CourtType == courtType &&
                x.TownId == null &&
                x.Town == town
            );
        }

        /// <summary>
        /// ✅ Citation auto-generator (server-side) + uniqueness enforcement.
        ///
        /// Format (matches your examples closely):
        /// [YEAR] {SERIES} ({COURT}-{TOWNCODE}) {TAIL}
        ///
        /// Example:
        /// [2016] LLR (HCK-K) 045/2013
        ///
        /// - SERIES derives from Service (LLR/EALR/ULR etc)
        /// - COURT derives from CourtType (HCK/CAK/SC/ELRC/ELC...)
        /// - TOWNCODE is first letter of Town (Kakamega=>K, Malindi=>M) if available
        /// - TAIL is CaseNumber if present else ReportNumber
        ///
        /// Uniqueness:
        /// - If collision, appends " (2)", " (3)" etc.
        /// - excludeReportId is used on update to ignore the current row
        /// </summary>
        private async Task<string> GenerateUniqueCitationAsync(LawReportUpsertDto dto, string? resolvedTownName, int? excludeReportId)
        {
            var series = ServiceCode(dto.Service);
            var court = CourtCode((CourtType)dto.CourtType);
            var townCode = TownCode(resolvedTownName);

            var tail = TrimOrNull(dto.CaseNumber) ?? dto.ReportNumber.Trim();
            tail = tail.Trim();

            var citationYear = dto.DecisionDate?.Year ?? dto.Year;
            var baseCitation = $"[{citationYear}] {series} ({court}{(string.IsNullOrWhiteSpace(townCode) ? "" : "-" + townCode)}) {tail}";
            baseCitation = NormalizeCitation(baseCitation);

            // Fast path: not taken
            if (!await CitationExistsAsync(baseCitation, excludeReportId))
                return baseCitation;

            // Collision: try " (2)", " (3)"...
            for (var i = 2; i <= 200; i++)
            {
                var c = $"{baseCitation} ({i})";
                if (!await CitationExistsAsync(c, excludeReportId))
                    return c;
            }

            // Extremely unlikely fallback
            return $"{baseCitation} ({Guid.NewGuid().ToString("N")[..8]})";
        }

        private async Task<bool> CitationExistsAsync(string citation, int? excludeReportId)
        {
            var q = _db.LawReports.AsNoTracking().Where(x => x.Citation == citation);
            if (excludeReportId.HasValue)
                q = q.Where(x => x.Id != excludeReportId.Value);

            return await q.AnyAsync();
        }

        private static string NormalizeCitation(string s)
        {
            s = (s ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) return s;

            // collapse whitespace
            while (s.Contains("  "))
                s = s.Replace("  ", " ");

            return s.Trim();
        }

        private static string ServiceCode(ReportService service) => service switch
        {
            ReportService.LawAfricaLawReports_LLR => "LLR",
            ReportService.OdungasDigest => "OD",
            ReportService.UgandaLawReports_ULR => "ULR",
            ReportService.TanzaniaLawReports_TLR => "TLR",
            ReportService.SouthernSudanLawReportsAndJournal_SSLRJ => "SSLRJ",
            ReportService.EastAfricaLawReports_EALR => "EALR",
            ReportService.EastAfricaCourtOfAppealReports_EACA => "EACA",
            ReportService.EastAfricaGeneralReports_EAGR => "EAGR",
            ReportService.EastAfricaProtectorateLawReports_EAPLR => "EAPLR",
            ReportService.ZanzibarProtectorateLawReports_ZPLR => "ZPLR",
            ReportService.CompanyRegistrySearch => "CRS",
            ReportService.UgandaLawSocietyReports_ULSR => "ULSR",
            ReportService.KenyaIndustrialPropertyInstitute => "KIPI",
            _ => "—"
        };

        private static string CourtCode(CourtType ct) => ct switch
        {
            CourtType.SupremeCourt => "SCK",
            CourtType.CourtOfAppeal => "CAK",
            CourtType.HighCourt => "HCK",
            CourtType.EmploymentAndLabourRelationsCourt => "ELRC",
            CourtType.EnvironmentAndLandCourt => "ELC",
            CourtType.MagistratesCourts => "MC",
            CourtType.KadhisCourts => "KC",
            CourtType.CourtsMartial => "CM",
            CourtType.SmallClaimsCourt => "SCC",
            CourtType.Tribunals => "TRIB",
            _ => "—"
        };

        private static string TownCode(string? town)
        {
            var t = TrimOrNull(town);
            if (string.IsNullOrWhiteSpace(t)) return "";

            // get first alphabetic character
            foreach (var ch in t.ToUpperInvariant())
            {
                if (ch >= 'A' && ch <= 'Z')
                    return ch.ToString();
            }
            return "";
        }

        private static string BuildReportTitle(LawReportUpsertDto dto, string? resolvedTownName)
        {
            var parts = new List<string> { $"{dto.ReportNumber.Trim()} ({dto.Year})" };

            if (!string.IsNullOrWhiteSpace(dto.Parties))
                parts.Add(dto.Parties.Trim());

            if (!string.IsNullOrWhiteSpace(dto.Citation))
                parts.Add(dto.Citation.Trim());

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

        private static string? TrimOrNull(string? s)
        {
            var t = (s ?? "").Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        // -------------------------
        // DTO mapping + LABELS
        // -------------------------
        private LawReportDto ToDto(LawReport r, bool includeContent)
        {
            var dto = new LawReportDto
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

            return dto;
        }

        // -------------------------
        // LABEL HELPERS
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
    }
}
