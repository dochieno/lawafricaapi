using LawAfrica.API.Data;
using LawAfrica.API.Models.Tax;
using LawAfrica.API.Models.Tax.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/vat-rates")]
    [Authorize(Roles = "Admin")]
    public class VatRatesAdminController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public VatRatesAdminController(ApplicationDbContext db) => _db = db;

        [HttpGet]
        public async Task<ActionResult<List<VatRateDto>>> List(CancellationToken ct)
        {
            var items = await _db.VatRates
                .OrderByDescending(x => x.IsActive).ThenBy(x => x.Code)
                .Select(x => new VatRateDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    RatePercent = x.RatePercent,
                    CountryScope = x.CountryScope,
                    IsActive = x.IsActive,
                    EffectiveFrom = x.EffectiveFrom,
                    EffectiveTo = x.EffectiveTo
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        [HttpPost]
        public async Task<ActionResult<VatRateDto>> Create([FromBody] VatRateDto dto, CancellationToken ct)
        {
            var code = (dto.Code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code)) return BadRequest("Code is required.");

            if (await _db.VatRates.AnyAsync(x => x.Code == code, ct))
                return BadRequest("A VAT code with this Code already exists.");

            var now = DateTime.UtcNow;
            var e = new VatRate
            {
                Code = code,
                Name = (dto.Name ?? "").Trim(),
                RatePercent = dto.RatePercent,
                CountryScope = string.IsNullOrWhiteSpace(dto.CountryScope) ? null : dto.CountryScope.Trim(),
                IsActive = dto.IsActive,
                EffectiveFrom = dto.EffectiveFrom,
                EffectiveTo = dto.EffectiveTo,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _db.VatRates.Add(e);
            await _db.SaveChangesAsync(ct);

            dto.Id = e.Id;
            return Ok(dto);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<VatRateDto>> Update(int id, [FromBody] VatRateDto dto, CancellationToken ct)
        {
            var e = await _db.VatRates.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e == null) return NotFound();

            var code = (dto.Code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code)) return BadRequest("Code is required.");

            if (await _db.VatRates.AnyAsync(x => x.Id != id && x.Code == code, ct))
                return BadRequest("Another VAT rate already uses this Code.");

            e.Code = code;
            e.Name = (dto.Name ?? "").Trim();
            e.RatePercent = dto.RatePercent;
            e.CountryScope = string.IsNullOrWhiteSpace(dto.CountryScope) ? null : dto.CountryScope.Trim();
            e.IsActive = dto.IsActive;
            e.EffectiveFrom = dto.EffectiveFrom;
            e.EffectiveTo = dto.EffectiveTo;
            e.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            dto.Id = e.Id;
            return Ok(dto);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var e = await _db.VatRates.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e == null) return NotFound();

            _db.VatRates.Remove(e);
            await _db.SaveChangesAsync(ct);
            return Ok();
        }
    }
}
