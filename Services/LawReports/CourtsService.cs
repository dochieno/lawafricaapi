using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models.LawReports.DTOs;
using LawAfrica.API.Models.LawReports.Models;
using Microsoft.EntityFrameworkCore;

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
    }
}
