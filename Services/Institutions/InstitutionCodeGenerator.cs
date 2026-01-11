using System.Security.Cryptography;
using LawAfrica.API.Data;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Institutions
{
    /// <summary>
    /// Generates a unique random institution access code:
    /// - 10 characters
    /// - Uppercase letters + digits
    /// - No confusing chars (O/0, I/1) to reduce typing errors
    /// Example: K8ZQ3W7H2M
    /// </summary>
    public class InstitutionAccessCodeGenerator
    {
        private readonly ApplicationDbContext _db;

        private const int Length = 10;

        // Uppercase + digits, excluding confusing characters
        private static readonly char[] Alphabet =
            "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

        public InstitutionAccessCodeGenerator(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<string> GenerateUniqueAsync(CancellationToken ct = default)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                var code = GenerateRandomCode(Length);

                var exists = await _db.Institutions
                    .AsNoTracking()
                    .AnyAsync(i => i.InstitutionAccessCode == code, ct);

                if (!exists)
                    return code;
            }

            throw new InvalidOperationException("Unable to generate a unique institution access code. Please try again.");
        }

        private static string GenerateRandomCode(int length)
        {
            var bytes = RandomNumberGenerator.GetBytes(length);
            var chars = new char[length];

            for (int i = 0; i < length; i++)
                chars[i] = Alphabet[bytes[i] % Alphabet.Length];

            return new string(chars);
        }
    }
}
