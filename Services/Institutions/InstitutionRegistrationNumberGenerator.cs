using LawAfrica.API.Data;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Institutions
{
    /// <summary>
    /// Generates sequential RegistrationNumber in the format:
    /// INSR-1000000000, INSR-1000000001, ...
    /// </summary>
    public class InstitutionRegistrationNumberGenerator
    {
        private readonly ApplicationDbContext _db;

        private const string Prefix = "INSR-";
        private const long StartNumber = 1_000_000_000L;
        private const long EndNumber = 9_999_999_999L;

        public InstitutionRegistrationNumberGenerator(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<string> GenerateNextAsync(CancellationToken ct = default)
        {
            // pull candidates once; you likely have few institutions so this is fine.
            var existing = await _db.Institutions
                .AsNoTracking()
                .Where(i => i.RegistrationNumber != null && i.RegistrationNumber.StartsWith(Prefix))
                .Select(i => i.RegistrationNumber!)
                .ToListAsync(ct);

            long max = StartNumber - 1;

            foreach (var s in existing)
            {
                var part = s.Substring(Prefix.Length);
                if (long.TryParse(part, out var n))
                    max = Math.Max(max, n);
            }

            var next = max + 1;
            if (next < StartNumber) next = StartNumber;
            if (next > EndNumber) throw new InvalidOperationException("Registration number range exhausted.");

            return $"{Prefix}{next}";
        }
    }
}
