using LawAfrica.API.Data;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Ai.Commentary
{
    public class UserJurisdictionResolver : IUserJurisdictionResolver
    {
        private readonly ApplicationDbContext _db;

        public UserJurisdictionResolver(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<UserJurisdictionContext> ResolveAsync(int userId, CancellationToken ct)
        {
            // Pull user + institution country ids + identity fields in one query
            var u = await _db.Users
                .AsNoTracking()
                .Where(x => x.Id == userId)
                .Select(x => new
                {
                    x.CountryId,
                    InstCountryId = x.Institution != null ? x.Institution.CountryId : (int?)null,
                    x.FirstName,
                    x.LastName,
                    x.Username,
                    x.City
                })
                .FirstOrDefaultAsync(ct);

            if (u == null)
            {
                return new UserJurisdictionContext
                {
                    CountryId = null,
                    CountryName = null,
                    CountryIso = null,
                    RegionLabel = "Africa",
                    City = null,
                    DisplayName = null
                };
            }

            var effectiveCountryId = u.CountryId ?? u.InstCountryId;

            string? displayName = $"{(u.FirstName ?? "").Trim()} {(u.LastName ?? "").Trim()}".Trim();
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = (u.Username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = null;

            if (effectiveCountryId == null)
            {
                // No country set -> return "unknown" but do NOT pretend Kenya
                return new UserJurisdictionContext
                {
                    CountryId = null,
                    CountryName = null,
                    CountryIso = null,
                    RegionLabel = "Africa",
                    City = string.IsNullOrWhiteSpace(u.City) ? null : u.City.Trim(),
                    DisplayName = displayName
                };
            }

            var c = await _db.Countries
                .AsNoTracking()
                .Where(x => x.Id == effectiveCountryId.Value)
                .Select(x => new { x.Id, x.Name, x.IsoCode })
                .FirstOrDefaultAsync(ct);

            var iso = (c?.IsoCode ?? "").Trim().ToUpperInvariant();
            var region = InferRegionFromIso(iso) ?? "Africa";

            return new UserJurisdictionContext
            {
                CountryId = c?.Id,
                CountryName = string.IsNullOrWhiteSpace(c?.Name) ? null : c!.Name.Trim(),
                CountryIso = string.IsNullOrWhiteSpace(iso) ? null : iso,
                RegionLabel = region,
                City = string.IsNullOrWhiteSpace(u.City) ? null : u.City.Trim(),
                DisplayName = displayName
            };
        }

        private static string? InferRegionFromIso(string? iso)
        {
            var x = (iso ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(x)) return null;

            if (x is "KE" or "UG" or "TZ" or "RW" or "BI" or "SS" or "ET" or "SO")
                return "East Africa";

            if (x is "NG" or "GH" or "SN" or "CI")
                return "West Africa";

            if (x is "ZA" or "BW" or "ZW" or "ZM" or "NA")
                return "Southern Africa";

            if (x is "EG" or "MA" or "TN" or "DZ")
                return "North Africa";

            return "Africa";
        }
    }
}