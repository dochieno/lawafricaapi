using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models.LawReports.DTOs;
using LawAfrica.API.Models.LawReports.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;


namespace LawAfrica.API.Services.LawReports
{
    public class CourtsService
    {
        private readonly ApplicationDbContext _db;

        private static readonly HashSet<string> AllowedCategories =
            new(StringComparer.OrdinalIgnoreCase) { "Criminal", "Civil", "Environmental", "Labour" };

        public CourtsService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<List<CourtDto>> AdminListAsync(
            int? countryId,
            string? q,
            bool includeInactive,
            CancellationToken ct)
        {
            q = (q ?? "").Trim();

            var query = _db.Courts
                .AsNoTracking()
                .AsQueryable();

            if (countryId.HasValue && countryId.Value > 0)
                query = query.Where(x => x.CountryId == countryId.Value);

            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(x =>
                    (x.Code != null && x.Code.Contains(q)) ||
                    (x.Name != null && x.Name.Contains(q)) ||
                    (x.Category != null && x.Category.Contains(q)) ||
                    (x.Abbreviation != null && x.Abbreviation.Contains(q))
                );
            }

            var list = await query
                .OrderBy(x => x.CountryId)
                .ThenBy(x => x.DisplayOrder)
                .ThenBy(x => x.Level ?? 999)
                .ThenBy(x => x.Name)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            return list.Select(ToDto).ToList();
        }

        public async Task<CourtDto?> GetAsync(int id, CancellationToken ct)
        {
            var entity = await _db.Courts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return entity == null ? null : ToDto(entity);
        }

        public async Task<CourtDto> CreateAsync(CourtUpsertDto dto, CancellationToken ct)
        {
            Validate(dto);

            // Ensure country exists + has IsoCode (generator requires it)
            var country = await _db.Countries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.CountryId, ct);
            if (country == null)
                throw new InvalidOperationException("Selected CountryId does not exist.");

            var code = (dto.Code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                code = await CourtCodeGenerator.GenerateAsync(_db, dto.CountryId, ct);
            }
            else
            {
                code = code.ToUpperInvariant();
            }

            // Duplicate protection (friendly message)
            var exists = await _db.Courts.AsNoTracking()
                .AnyAsync(x => x.CountryId == dto.CountryId && x.Code == code, ct);
            if (exists)
                throw new InvalidOperationException($"Court code '{code}' already exists for this country.");

            var name = dto.Name.Trim();

            var nameExists = await _db.Courts.AsNoTracking()
                .AnyAsync(x => x.CountryId == dto.CountryId && x.Name == name, ct);
            if (nameExists)
                throw new InvalidOperationException($"Court name '{name}' already exists for this country.");

            var entity = new Court
            {
                CountryId = dto.CountryId,
                Code = code,
                Name = name,
                Category = NormalizeCategory(dto.Category),
                Abbreviation = TrimOrNull(dto.Abbreviation),
                Level = dto.Level,
                IsActive = dto.IsActive,
                DisplayOrder = dto.DisplayOrder,
                Notes = TrimOrNull(dto.Notes),
                CreatedAt = DateTime.UtcNow
            };

            _db.Courts.Add(entity);
            await _db.SaveChangesAsync(ct);

            return ToDto(entity);
        }

        public async Task UpdateAsync(int id, CourtUpsertDto dto, CancellationToken ct)
        {
            Validate(dto);

            var entity = await _db.Courts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity == null)
                throw new KeyNotFoundException("Court not found.");

            // Do NOT allow changing country via update (keeps code uniqueness sane)
            if (dto.CountryId != entity.CountryId)
                throw new InvalidOperationException("CountryId cannot be changed for an existing court.");

            // Do NOT allow changing Code once created
            var incomingCode = (dto.Code ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(incomingCode))
            {
                incomingCode = incomingCode.ToUpperInvariant();
                if (!string.Equals(incomingCode, entity.Code, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Code cannot be changed once set.");
            }

            var newName = dto.Name.Trim();
            if (!string.Equals(newName, entity.Name, StringComparison.OrdinalIgnoreCase))
            {
                var nameExists = await _db.Courts.AsNoTracking()
                    .AnyAsync(x => x.CountryId == entity.CountryId && x.Name == newName && x.Id != id, ct);
                if (nameExists)
                    throw new InvalidOperationException($"Court name '{newName}' already exists for this country.");
            }

            entity.Name = newName;
            entity.Category = NormalizeCategory(dto.Category);
            entity.Abbreviation = TrimOrNull(dto.Abbreviation);
            entity.Level = dto.Level;
            entity.IsActive = dto.IsActive;
            entity.DisplayOrder = dto.DisplayOrder;
            entity.Notes = TrimOrNull(dto.Notes);
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
        }

        public async Task DeleteAsync(int id, CancellationToken ct)
        {
            var entity = await _db.Courts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity == null)
                throw new KeyNotFoundException("Court not found.");

            // Guard: if linked to LawReports (CourtId), don't delete—force deactivate instead
            var used = await _db.LawReports.AsNoTracking().AnyAsync(r => r.CourtId == id, ct);
            if (used)
                throw new InvalidOperationException("Court is in use by law reports. Deactivate it instead of deleting.");

            _db.Courts.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }

        // -------------------------
        // Helpers
        // -------------------------
        private static void Validate(CourtUpsertDto dto)
        {
            if (dto.CountryId <= 0) throw new InvalidOperationException("CountryId is required.");
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("Name is required.");
            if (string.IsNullOrWhiteSpace(dto.Category)) throw new InvalidOperationException("Category is required.");

            var cat = dto.Category.Trim();
            if (!AllowedCategories.Contains(cat))
                throw new InvalidOperationException("Category must be one of: Criminal, Civil, Environmental, Labour.");
        }

        private static string NormalizeCategory(string raw)
        {
            var t = (raw ?? "").Trim();
            if (t.Equals("criminal", StringComparison.OrdinalIgnoreCase)) return "Criminal";
            if (t.Equals("civil", StringComparison.OrdinalIgnoreCase)) return "Civil";
            if (t.Equals("environmental", StringComparison.OrdinalIgnoreCase)) return "Environmental";
            if (t.Equals("labour", StringComparison.OrdinalIgnoreCase)) return "Labour";
            return t; // validation should have caught bad values
        }

        private static string? TrimOrNull(string? s)
        {
            var t = (s ?? "").Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        private static CourtDto ToDto(Court x) => new CourtDto
        {
            Id = x.Id,
            CountryId = x.CountryId,
            Code = x.Code,
            Name = x.Name,
            Category = x.Category,
            Abbreviation = x.Abbreviation,
            Level = x.Level,
            IsActive = x.IsActive,
            DisplayOrder = x.DisplayOrder,
            Notes = x.Notes,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        };

        public async Task<CourtsImportResultDto> ImportCsvAsync(
            IFormFile file,
            int countryId,
            string mode, // "upsert" | "createonly"
            CancellationToken ct)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("CSV file is required.");

            // normalize mode
            mode = (mode ?? "").Trim().ToLowerInvariant();
            var createOnly = mode == "createonly";
            if (mode != "createonly" && mode != "upsert") mode = "upsert";

            // Validate country exists
            var countryExists = await _db.Countries.AnyAsync(c => c.Id == countryId, ct);
            if (!countryExists) throw new InvalidOperationException("Country not found.");

            var result = new CourtsImportResultDto
            {
                CountryId = countryId,
                Mode = mode
            };

            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);

            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
                IgnoreBlankLines = true,
                BadDataFound = null,
                MissingFieldFound = null,
                HeaderValidated = null,
                DetectColumnCountChanges = false,
                PrepareHeaderForMatch = args => (args.Header ?? "").Trim()
            };

            using var csv = new CsvReader(reader, csvConfig);

            if (!await csv.ReadAsync()) return result;
            csv.ReadHeader();

            bool Has(string name) =>
                csv.HeaderRecord?.Any(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase)) == true;

            string? Get(string name)
            {
                if (!Has(name)) return null;
                var v = csv.GetField(name);
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }

            int rowNumber = 0;

            while (await csv.ReadAsync())
            {
                ct.ThrowIfCancellationRequested();
                rowNumber++;
                result.TotalRows++;

                try
                {
                    var name = Get("Name");
                    var code = Get("Code"); // optional
                    var abbreviation = Get("Abbreviation") ?? Get("Abbrev") ?? Get("Abbr");
                    var notes = Get("Notes");

                    // ✅ Category: optional column, default Civil
                    var categoryRaw = Get("Category");
                    var category = string.IsNullOrWhiteSpace(categoryRaw) ? "Civil" : categoryRaw.Trim();

                    // numeric fields
                    int? level = null;
                    var levelRaw = Get("Level");
                    if (!string.IsNullOrWhiteSpace(levelRaw) && int.TryParse(levelRaw, out var lvl)) level = lvl;

                    int displayOrder = 0;
                    var orderRaw = Get("DisplayOrder") ?? Get("Order");
                    if (!string.IsNullOrWhiteSpace(orderRaw) && int.TryParse(orderRaw, out var ord)) displayOrder = ord;

                    bool isActive = true;
                    var activeRaw = Get("IsActive") ?? Get("Active");
                    if (!string.IsNullOrWhiteSpace(activeRaw))
                    {
                        if (bool.TryParse(activeRaw, out var b)) isActive = b;
                        else if (int.TryParse(activeRaw, out var n)) isActive = n != 0;
                    }

                    if (string.IsNullOrWhiteSpace(name))
                        throw new InvalidOperationException("Name is required.");

                    // Find existing by (Country+Code) else (Country+Name)
                    var query = _db.Courts.AsQueryable().Where(c => c.CountryId == countryId);

                    Court? existing = null;

                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        var codeNorm = code.Trim().ToUpperInvariant(); // ✅ normalize for matching
                        existing = await query.FirstOrDefaultAsync(c => c.Code == codeNorm, ct);
                    }

                    if (existing == null)
                    {
                        var nameNorm = name.Trim();
                        existing = await query.FirstOrDefaultAsync(
                            c => c.Name.ToLower() == nameNorm.ToLower(),
                            ct
                        );
                    }

                    if (existing == null)
                    {
                        // Create (CreateAsync does auto-code when Code is null/blank)
                        var dto = new CourtUpsertDto
                        {
                            CountryId = countryId,
                            Code = string.IsNullOrWhiteSpace(code) ? null : code.Trim(),
                            Name = name.Trim(),
                            Category = category, // ✅ must be valid; defaults Civil
                            Abbreviation = abbreviation,
                            Level = level,
                            DisplayOrder = displayOrder,
                            IsActive = isActive,
                            Notes = notes
                        };

                        await CreateAsync(dto, ct);
                        result.Created++;
                    }
                    else
                    {
                        if (createOnly)
                        {
                            result.Skipped++;
                            continue;
                        }

                        var dto = new CourtUpsertDto
                        {
                            CountryId = countryId,
                            Code = existing.Code, // keep stable
                            Name = name.Trim(),
                            Category = category,  // ✅ keep valid
                            Abbreviation = abbreviation,
                            Level = level,
                            DisplayOrder = displayOrder,
                            IsActive = isActive,
                            Notes = notes
                        };

                        await UpdateAsync(existing.Id, dto, ct);
                        result.Updated++;
                    }
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add(new CourtsImportErrorDto
                    {
                        Row = rowNumber,
                        Message = ex.Message,
                        Name = SafePeek(csv, "Name"),
                        Code = SafePeek(csv, "Code"),
                    });
                }
            }

            return result;

            static string? SafePeek(CsvReader csv, string col)
            {
                try
                {
                    var v = csv.GetField(col);
                    return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
                }
                catch { return null; }
            }
        }


    }
}
