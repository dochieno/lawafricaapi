using LawAfrica.API.Data;
using LawAfrica.API.Models.Lawyers;
using Microsoft.EntityFrameworkCore;
using System;

namespace LawAfrica.API.Services.Lawyers
{
    public class LawyerDirectoryService : ILawyerDirectoryService
    {
        private readonly ApplicationDbContext _db; // rename to your actual DbContext class

        public LawyerDirectoryService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<LawyerProfile?> GetLawyerAsync(int lawyerProfileId, CancellationToken ct = default)
        {
            return await _db.LawyerProfiles
                .Include(x => x.User)
                .Include(x => x.HighestCourtAllowed)
                .Include(x => x.PrimaryTown)!.ThenInclude(t => t.Country)
                .Include(x => x.PracticeAreas).ThenInclude(pa => pa.PracticeArea)
                .Include(x => x.TownsServed).ThenInclude(lt => lt.Town)
                .FirstOrDefaultAsync(x => x.Id == lawyerProfileId && x.IsActive, ct);
        }

        public async Task<List<LawyerProfile>> SearchLawyersAsync(
            int? countryId,
            int? townId,
            int? practiceAreaId,
            int? highestCourtAllowedId,
            bool verifiedOnly,
            string? q,
            int take = 30,
            int skip = 0,
            CancellationToken ct = default)
        {
            var query = _db.LawyerProfiles
                .AsNoTracking()
                .Where(x => x.IsActive);

            if (verifiedOnly)
                query = query.Where(x => x.VerificationStatus == LawyerVerificationStatus.Verified);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(x =>
                    x.DisplayName.Contains(term) ||
                    (x.FirmName != null && x.FirmName.Contains(term)));
            }

            if (highestCourtAllowedId.HasValue)
                query = query.Where(x => x.HighestCourtAllowedId == highestCourtAllowedId.Value);

            if (practiceAreaId.HasValue)
                query = query.Where(x => x.PracticeAreas.Any(p => p.PracticeAreaId == practiceAreaId.Value));

            if (townId.HasValue)
                query = query.Where(x =>
                    x.PrimaryTownId == townId.Value ||
                    x.TownsServed.Any(t => t.TownId == townId.Value));

            if (countryId.HasValue)
            {
                query = query.Where(x =>
                    (x.PrimaryTown != null && x.PrimaryTown.CountryId == countryId.Value) ||
                    x.TownsServed.Any(ts => ts.Town.CountryId == countryId.Value));
            }

            return await query
                .OrderByDescending(x => x.VerificationStatus == LawyerVerificationStatus.Verified)
                .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Include(x => x.HighestCourtAllowed)
                .Include(x => x.PrimaryTown)
                .Include(x => x.PracticeAreas).ThenInclude(pa => pa.PracticeArea)
                .ToListAsync(ct);
        }
    }
}