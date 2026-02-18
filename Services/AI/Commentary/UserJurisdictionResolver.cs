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
            // Load user's country. If missing, fallback to institution country.
            var u = await _db.Users
                .AsNoTracking()
                .Where(x => x.Id == userId)
                .Select(x => new
                {
                    x.CountryId,
                    InstitutionCountryId = x.Institution != null ? x.Institution.CountryId : (int?)null
                })
                .FirstOrDefaultAsync(ct);

            var countryId = u?.CountryId ?? u?.InstitutionCountryId;

            // If still unknown, default to Kenya (or first seed) but mark as unknown.
            // You can change this default anytime.
            if (countryId == null)
            {
                return new UserJurisdictionContext
                {
                    CountryId = null,
                    CountryName = "Unknown",
                    CountryIso = "",
                    RegionLabel = "Unknown",
                    RegionCountryIds = EastAfricaIds(),
                    AfricaCountryIds = await GetAllCountryIds(ct)
                };
            }

            var c = await _db.Countries.AsNoTracking()
                .Where(x => x.Id == countryId.Value)
                .Select(x => new { x.Id, x.Name, x.IsoCode })
                .FirstOrDefaultAsync(ct);

            var africaIds = await GetAllCountryIds(ct);
            var regionIds = ResolveRegion(countryId.Value);

            return new UserJurisdictionContext
            {
                CountryId = c?.Id,
                CountryName = c?.Name ?? "Unknown",
                CountryIso = c?.IsoCode ?? "",
                RegionLabel = "East Africa", // current implementation
                RegionCountryIds = regionIds,
                AfricaCountryIds = africaIds
            };
        }

        private async Task<List<int>> GetAllCountryIds(CancellationToken ct)
        {
            return await _db.Countries.AsNoTracking()
                .Select(x => x.Id)
                .ToListAsync(ct);
        }

        // For now, your platform seed includes KE/UG/TZ/RW/ZA.
        // East Africa includes KE, UG, TZ, RW (+ Burundi, South Sudan if you add them later).
        private static List<int> ResolveRegion(int userCountryId)
        {
            // If later you add a Region table, we replace this with DB-driven mapping.
            return EastAfricaIds();
        }

        private static List<int> EastAfricaIds()
        {
            // With your current seed:
            // KE=1, UG=2, TZ=3, RW=4 (from your seed)
            return new List<int> { 1, 2, 3, 4 };
        }
    }
}
