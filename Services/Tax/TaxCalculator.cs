using LawAfrica.API.Data;
using LawAfrica.API.Models.Tax;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Tax
{
    public class TaxQuote
    {
        public string VatCode { get; set; } = "VAT0";
        public decimal VatRatePercent { get; set; } = 0m;
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal GrossAmount { get; set; }
    }

    public interface ITaxCalculator
    {
        Task<VatRate?> ResolveVatRateAsync(string purpose, string? countryCode, int? explicitVatRateId, CancellationToken ct);
        TaxQuote Compute(decimal netAmount, VatRate? vatRate);
    }

    public class TaxCalculator : ITaxCalculator
    {
        private readonly ApplicationDbContext _db;

        public TaxCalculator(ApplicationDbContext db) => _db = db;

        public async Task<VatRate?> ResolveVatRateAsync(string purpose, string? countryCode, int? explicitVatRateId, CancellationToken ct)
        {
            // 1) Explicit VAT on item/document wins (if active + effective)
            if (explicitVatRateId.HasValue)
            {
                var vr = await _db.VatRates.FirstOrDefaultAsync(x => x.Id == explicitVatRateId.Value, ct);
                if (IsUsable(vr)) return vr;
            }

            var c = (countryCode ?? "").Trim().ToUpperInvariant();

            // 2) Rule match by purpose + country first, then purpose + "*" then purpose + null
            var now = DateTime.UtcNow;

            var rule = await _db.VatRules
                .Include(r => r.VatRate)
                .Where(r => r.IsActive && r.Purpose == purpose)
                .Where(r => r.EffectiveFrom == null || r.EffectiveFrom <= now)
                .Where(r => r.EffectiveTo == null || r.EffectiveTo >= now)
                .OrderByDescending(r => r.Priority)
                .FirstOrDefaultAsync(r =>
                    (!string.IsNullOrEmpty(c) && r.CountryCode == c) ||
                    r.CountryCode == "*" ||
                    r.CountryCode == null, ct);

            if (rule?.VatRate != null && IsUsable(rule.VatRate))
                return rule.VatRate;

            // 3) Hard default: Kenya registration VAT16 if configured; otherwise no VAT
            if (purpose == "RegistrationFee" && c == "KE")
            {
                var vat16 = await _db.VatRates.FirstOrDefaultAsync(x => x.Code == "VAT16", ct);
                if (IsUsable(vat16)) return vat16;
            }

            return null; // means VAT 0
        }

        public TaxQuote Compute(decimal netAmount, VatRate? vatRate)
        {
            var rate = vatRate?.RatePercent ?? 0m;
            var vat = Math.Round(netAmount * (rate / 100m), 2, MidpointRounding.AwayFromZero);
            var gross = netAmount + vat;

            return new TaxQuote
            {
                VatCode = vatRate?.Code ?? "VAT0",
                VatRatePercent = rate,
                NetAmount = netAmount,
                VatAmount = vat,
                GrossAmount = gross
            };
        }

        private static bool IsUsable(VatRate? vr)
        {
            if (vr == null) return false;
            if (!vr.IsActive) return false;
            var now = DateTime.UtcNow;
            if (vr.EffectiveFrom != null && vr.EffectiveFrom > now) return false;
            if (vr.EffectiveTo != null && vr.EffectiveTo < now) return false;
            return true;
        }
    }
}
