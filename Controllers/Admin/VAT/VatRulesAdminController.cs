using LawAfrica.API.Data;
using LawAfrica.API.Models.Tax;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/vat-rules")]
    [Authorize(Roles = "Admin")]
    public class VatRulesAdminController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public VatRulesAdminController(ApplicationDbContext db) => _db = db;

        [HttpGet]
        public async Task<ActionResult<List<VatRuleDto>>> List(CancellationToken ct)
        {
            var items = await _db.VatRules
                .Include(x => x.VatRate)
                .OrderByDescending(x => x.IsActive)
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => x.Purpose)
                .Select(x => new VatRuleDto
                {
                    Id = x.Id,
                    Purpose = x.Purpose,
                    CountryCode = x.CountryCode,
                    VatRateId = x.VatRateId,
                    VatRateCode = x.VatRate!.Code,
                    VatRatePercent = x.VatRate.RatePercent,
                    IsActive = x.IsActive,
                    Priority = x.Priority,
                    EffectiveFrom = x.EffectiveFrom,
                    EffectiveTo = x.EffectiveTo
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        [HttpPost]
        public async Task<ActionResult<VatRuleDto>> Create([FromBody] VatRuleDto dto, CancellationToken ct)
        {
            var purpose = (dto.Purpose ?? "").Trim();
            if (string.IsNullOrWhiteSpace(purpose)) return BadRequest("Purpose is required.");
            if (dto.VatRateId <= 0) return BadRequest("VatRateId is required.");

            var existsVat = await _db.VatRates.AnyAsync(x => x.Id == dto.VatRateId, ct);
            if (!existsVat) return BadRequest("Invalid VatRateId.");

            var now = DateTime.UtcNow;
            var e = new VatRule
            {
                Purpose = purpose,
                CountryCode = string.IsNullOrWhiteSpace(dto.CountryCode) ? null : dto.CountryCode.Trim(),
                VatRateId = dto.VatRateId,
                Priority = dto.Priority,
                IsActive = dto.IsActive,
                EffectiveFrom = dto.EffectiveFrom,
                EffectiveTo = dto.EffectiveTo,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _db.VatRules.Add(e);
            await _db.SaveChangesAsync(ct);

            dto.Id = e.Id;
            return Ok(dto);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<VatRuleDto>> Update(int id, [FromBody] VatRuleDto dto, CancellationToken ct)
        {
            var e = await _db.VatRules.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e == null) return NotFound();

            var purpose = (dto.Purpose ?? "").Trim();
            if (string.IsNullOrWhiteSpace(purpose)) return BadRequest("Purpose is required.");
            if (dto.VatRateId <= 0) return BadRequest("VatRateId is required.");

            var existsVat = await _db.VatRates.AnyAsync(x => x.Id == dto.VatRateId, ct);
            if (!existsVat) return BadRequest("Invalid VatRateId.");

            e.Purpose = purpose;
            e.CountryCode = string.IsNullOrWhiteSpace(dto.CountryCode) ? null : dto.CountryCode.Trim();
            e.VatRateId = dto.VatRateId;
            e.Priority = dto.Priority;
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
            var e = await _db.VatRules.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e == null) return NotFound();

            _db.VatRules.Remove(e);
            await _db.SaveChangesAsync(ct);
            return Ok();
        }
    }
}
