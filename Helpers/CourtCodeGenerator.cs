using Microsoft.EntityFrameworkCore;
using LawAfrica.API.Data;
using LawAfrica.API.Models.LawReports.Models;

namespace LawAfrica.API.Helpers
{
    public static class CourtCodeGenerator
    {
        // Format: KE-CO-001
        public static async Task<string> GenerateAsync(
            ApplicationDbContext db,
            int countryId,
            CancellationToken ct = default)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var country = await db.Countries
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == countryId, ct);

            if (country == null)
                throw new InvalidOperationException($"CountryId={countryId} not found.");

            var iso = (country.IsoCode ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(iso))
                throw new InvalidOperationException(
                    $"Country '{country.Name}' does not have an IsoCode. Cannot generate court code.");

            var seq = await db.CourtSequences
                .FirstOrDefaultAsync(x => x.CountryId == countryId, ct);

            if (seq == null)
            {
                seq = new CourtSequence
                {
                    CountryId = countryId,
                    NextValue = 1,
                    UpdatedAt = DateTime.UtcNow
                };

                db.CourtSequences.Add(seq);
                await db.SaveChangesAsync(ct);
            }

            var next = seq.NextValue;

            seq.NextValue = next + 1;
            seq.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return $"{iso}-CO-{next:000}";
        }
    }
}
