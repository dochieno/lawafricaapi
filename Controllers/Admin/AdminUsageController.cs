using LawAfrica.API.Data;
using LawAfrica.API.Models.DTOs.AdminDashboard;
using LawAfrica.API.Models.Usage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/usage")]
    [Authorize(Roles = "Admin")]
    public class AdminUsageController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AdminUsageController(ApplicationDbContext db)
        {
            _db = db;
        }

        private sealed class DayCountRow
        {
            public DateTime DayUtc { get; set; }
            public int Count { get; set; }
        }

        private static string BuildWhereSql(
            AdminUsageSummaryQuery query,
            DateTime fromUtc,
            DateTime toUtc,
            string normalizedResult,
            out List<NpgsqlParameter> parameters)
        {
            parameters = new List<NpgsqlParameter>
            {
                new NpgsqlParameter("fromUtc", fromUtc),
                new NpgsqlParameter("toUtc", toUtc)
            };

            var whereSql = "WHERE \"AtUtc\" >= @fromUtc AND \"AtUtc\" < @toUtc";

            if (query.InstitutionId.HasValue)
            {
                whereSql += " AND \"InstitutionId\" = @institutionId";
                parameters.Add(new NpgsqlParameter("institutionId", query.InstitutionId.Value));
            }

            if (query.LegalDocumentId.HasValue)
            {
                whereSql += " AND \"LegalDocumentId\" = @legalDocumentId";
                parameters.Add(new NpgsqlParameter("legalDocumentId", query.LegalDocumentId.Value));
            }

            if (!string.IsNullOrWhiteSpace(query.DenyReason))
            {
                whereSql += " AND \"DecisionReason\" = @denyReason";
                parameters.Add(new NpgsqlParameter("denyReason", query.DenyReason.Trim()));
            }

            if (normalizedResult == "allowed")
                whereSql += " AND \"Allowed\" = TRUE";
            else if (normalizedResult == "denied")
                whereSql += " AND \"Allowed\" = FALSE";

            return whereSql;
        }

        private async Task<List<DayCountRow>> QueryDayCountsAsync(
            string sql,
            List<NpgsqlParameter> parameters)
        {
            var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
            var wasClosed = conn.State != ConnectionState.Open;

            if (wasClosed)
                await conn.OpenAsync();

            try
            {
                await using var cmd = new NpgsqlCommand(sql, conn);

                foreach (var p in parameters)
                    cmd.Parameters.Add(p);

                var rows = new List<DayCountRow>();
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var day = reader.GetDateTime(0);
                    var count = reader.GetInt32(1);

                    rows.Add(new DayCountRow
                    {
                        DayUtc = DateTime.SpecifyKind(day, DateTimeKind.Utc),
                        Count = count
                    });
                }

                return rows;
            }
            finally
            {
                if (wasClosed)
                    await conn.CloseAsync();
            }
        }

        [HttpGet("summary")]
        public async Task<ActionResult<AdminUsageSummaryResponse>> Summary([FromQuery] AdminUsageSummaryQuery query)
        {
            try
            {
                var now = DateTime.UtcNow;
                var toUtc = query.ToUtc?.ToUniversalTime() ?? now;
                var fromUtc = query.FromUtc?.ToUniversalTime() ?? toUtc.AddDays(-30);

                if (fromUtc >= toUtc)
                    return BadRequest("FromUtc must be earlier than ToUtc.");

                // -------------------------
                // BASE QUERY (EF)
                // -------------------------
                IQueryable<UsageEvent> evQ = _db.UsageEvents
                    .AsNoTracking()
                    .Where(e => e.AtUtc >= fromUtc && e.AtUtc < toUtc);

                if (query.InstitutionId.HasValue)
                    evQ = evQ.Where(e => e.InstitutionId == query.InstitutionId.Value);

                if (query.LegalDocumentId.HasValue)
                    evQ = evQ.Where(e => e.LegalDocumentId == query.LegalDocumentId.Value);

                var result = (query.Result ?? "").Trim().ToLowerInvariant();
                if (result == "allowed")
                    evQ = evQ.Where(e => e.Allowed);
                else if (result == "denied")
                    evQ = evQ.Where(e => !e.Allowed);

                if (!string.IsNullOrWhiteSpace(query.DenyReason))
                {
                    var deny = query.DenyReason.Trim();
                    evQ = evQ.Where(e => e.DecisionReason == deny);
                }

                // -------------------------
                // CORE METRICS
                // -------------------------
                var reads = await evQ.CountAsync(e => e.Allowed);
                var blocks = await evQ.CountAsync(e => !e.Allowed);

                // -------------------------
                // PER-DAY SERIES (RAW SQL)
                // -------------------------
                var whereSql = BuildWhereSql(query, fromUtc, toUtc, result, out var baseParams);

                string readsSql;
                string blocksSql;

                if (result == "allowed")
                {
                    readsSql = $@"
                    SELECT date_trunc('day', ""AtUtc"") AS ""DayUtc"", COUNT(*)::int AS ""Count""
                    FROM ""UsageEvents""
                    {whereSql}
                    GROUP BY 1
                    ORDER BY 1;";

                    blocksSql = $@"
                    SELECT date_trunc('day', ""AtUtc"") AS ""DayUtc"", COUNT(*)::int AS ""Count""
                    FROM ""UsageEvents""
                    {whereSql} AND 1=0
                    GROUP BY 1
                    ORDER BY 1;";
                }
                else if (result == "denied")
                {
                    readsSql = $@"
                    SELECT date_trunc('day', ""AtUtc"") AS ""DayUtc"", COUNT(*)::int AS ""Count""
                    FROM ""UsageEvents""
                    {whereSql} AND 1=0
                    GROUP BY 1
                    ORDER BY 1;";

                    blocksSql = $@"
                    SELECT date_trunc('day', ""AtUtc"") AS ""DayUtc"", COUNT(*)::int AS ""Count""
                    FROM ""UsageEvents""
                    {whereSql}
                    GROUP BY 1
                    ORDER BY 1;";
                }
                else
                {
                    readsSql = $@"
                    SELECT date_trunc('day', ""AtUtc"") AS ""DayUtc"", COUNT(*)::int AS ""Count""
                    FROM ""UsageEvents""
                    {whereSql} AND ""Allowed"" = TRUE
                    GROUP BY 1
                    ORDER BY 1;";

                    blocksSql = $@"
                    SELECT date_trunc('day', ""AtUtc"") AS ""DayUtc"", COUNT(*)::int AS ""Count""
                    FROM ""UsageEvents""
                    {whereSql} AND ""Allowed"" = FALSE
                    GROUP BY 1
                    ORDER BY 1;";
                }

                List<NpgsqlParameter> CloneParams() =>
                    baseParams.Select(p => new NpgsqlParameter(p.ParameterName, p.Value)).ToList();

                var readsByDayRaw = await QueryDayCountsAsync(readsSql, CloneParams());
                var blocksByDayRaw = await QueryDayCountsAsync(blocksSql, CloneParams());

                var readsByDay = readsByDayRaw
                    .Select(x => new DateValuePoint(x.DayUtc, x.Count))
                    .ToList();

                var blocksByDay = blocksByDayRaw
                    .Select(x => new DateValuePoint(x.DayUtc, x.Count))
                    .ToList();

                // -------------------------
                // BREAKDOWNS (FIXED: translatable first, map after)
                // -------------------------

                // Denies by reason
                var deniesRaw = await evQ
                    .Where(e => !e.Allowed)
                    .GroupBy(e => e.DecisionReason)
                    .Select(g => new { Key = g.Key, Cnt = g.Count() })
                    .OrderByDescending(x => x.Cnt)
                    .Take(20)
                    .ToListAsync();

                var deniesByReason = deniesRaw
                    .Select(x => new KeyValuePoint(x.Key, x.Cnt))
                    .ToList();

                // Top documents by reads (RAW IDs + counts)
                var topDocsRaw = await evQ
                    .Where(e => e.Allowed)
                    .GroupBy(e => e.LegalDocumentId)
                    .Select(g => new { Key = g.Key, Cnt = g.Count() })
                    .OrderByDescending(x => x.Cnt)
                    .Take(10)
                    .ToListAsync();

                // ✅ CHANGE START: resolve doc titles for topDocs
                var topDocIds = topDocsRaw.Select(x => x.Key).Distinct().ToList();

                var docTitles = await _db.LegalDocuments
                    .AsNoTracking()
                    .Where(d => topDocIds.Contains(d.Id))
                    .Select(d => new { d.Id, d.Title })
                    .ToListAsync();

                var docTitleMap = docTitles
                    .GroupBy(x => x.Id)
                    .ToDictionary(g => g.Key, g => g.First().Title ?? "");

                var topDocs = topDocsRaw
                    .Select(x =>
                    {
                        docTitleMap.TryGetValue(x.Key, out var title);
                        var label = !string.IsNullOrWhiteSpace(title) ? title : $"Document #{x.Key}";
                        return new KeyValuePoint(label, x.Cnt);
                    })
                    .ToList();
                // ✅ CHANGE END

                // Top institutions by reads (RAW IDs + counts)
                var topInstRaw = await evQ
                    .Where(e => e.Allowed && e.InstitutionId != null)
                    .GroupBy(e => e.InstitutionId!.Value)
                    .Select(g => new { Key = g.Key, Cnt = g.Count() })
                    .OrderByDescending(x => x.Cnt)
                    .Take(10)
                    .ToListAsync();

                // ✅ CHANGE START: resolve institution names for topInstitutions
                var topInstIds = topInstRaw.Select(x => x.Key).Distinct().ToList();

                var instNames = await _db.Institutions
                    .AsNoTracking()
                    .Where(i => topInstIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.Name })
                    .ToListAsync();

                var instNameMap = instNames
                    .GroupBy(x => x.Id)
                    .ToDictionary(g => g.Key, g => g.First().Name ?? "");

                var topInstitutions = topInstRaw
                    .Select(x =>
                    {
                        instNameMap.TryGetValue(x.Key, out var name);
                        var label = !string.IsNullOrWhiteSpace(name) ? name : $"Institution #{x.Key}";
                        return new KeyValuePoint(label, x.Cnt);
                    })
                    .ToList();
                // ✅ CHANGE END

                var total = reads + blocks;
                var blockRate = total == 0 ? 0m : Math.Round((decimal)blocks / total, 4);

                return Ok(new AdminUsageSummaryResponse(
                    FromUtc: fromUtc,
                    ToUtc: toUtc,
                    Reads: reads,
                    Blocks: blocks,
                    BlockRate: blockRate,
                    ReadsByDay: readsByDay,
                    BlocksByDay: blocksByDay,
                    DeniesByReason: deniesByReason,
                    TopDocumentsByReads: topDocs,
                    TopInstitutionsByReads: topInstitutions
                ));
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                return Problem(
                    title: "Usage analytics not initialized",
                    detail: "The UsageEvents table does not exist. Run database migrations and retry.",
                    statusCode: 500
                );
            }
            catch (Exception ex)
            {
                return Problem(
                    title: "Usage analytics query could not run",
                    detail: $"Something went wrong while building usage analytics. {ex.Message}",
                    statusCode: 500
                );
            }
        }
    }
}
