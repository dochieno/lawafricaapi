using LawAfrica.API.Data;
using LawAfrica.API.Models.Payments;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Payments
{
    /// <summary>
    /// Generates unique, sequential invoice numbers with SERIALIZABLE isolation for concurrency safety.
    /// Format: INV-YYYY-000001
    /// </summary>
    public class InvoiceNumberGenerator
    {
        private readonly ApplicationDbContext _db;

        public InvoiceNumberGenerator(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<string> GenerateAsync(CancellationToken ct = default)
        {
            var year = DateTime.UtcNow.Year;

            await using var tx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, ct);

            var seq = await _db.InvoiceSequences
                .FirstOrDefaultAsync(x => x.Year == year, ct);

            if (seq == null)
            {
                seq = new InvoiceSequence
                {
                    Year = year,
                    LastNumber = 0
                };

                _db.InvoiceSequences.Add(seq);
                await _db.SaveChangesAsync(ct);
            }

            seq.LastNumber += 1;
            seq.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return $"INV-{year}-{seq.LastNumber:D6}";
        }
    }
}
