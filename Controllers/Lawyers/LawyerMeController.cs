// =======================================================
// FILE: LawAfrica.API/Controllers/Lawyers/LawyerMeController.cs
// =======================================================
using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Lawyers;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models.Lawyers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/lawyers/me")]
    [Authorize]
    public class LawyerMeController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public LawyerMeController(ApplicationDbContext db)
        {
            _db = db;
        }

        // -------------------------
        // GET my lawyer profile (or null)
        // GET /api/lawyers/me
        // -------------------------
        [HttpGet]
        public async Task<IActionResult> GetMine(CancellationToken ct)
        {
            try
            {
                var userId = User.GetUserId();

                var p = await _db.LawyerProfiles
                    .AsNoTracking()
                    .Include(x => x.PrimaryTown)
                    .Include(x => x.HighestCourtAllowed)
                    .Include(x => x.PracticeAreas).ThenInclude(pa => pa.PracticeArea)
                    .Include(x => x.TownsServed).ThenInclude(ts => ts.Town)
                    .Include(x => x.ServiceOfferings).ThenInclude(s => s.LawyerService)
                    .FirstOrDefaultAsync(x => x.UserId == userId, ct);

                if (p == null) return Ok(null);

                return Ok(new
                {
                    id = p.Id,
                    userId = p.UserId,
                    displayName = p.DisplayName,
                    firmName = p.FirmName,
                    bio = p.Bio,
                    primaryPhone = p.PrimaryPhone,
                    publicEmail = p.PublicEmail,

                    primaryTownId = p.PrimaryTownId,
                    primaryTownName = p.PrimaryTown != null ? p.PrimaryTown.Name : null,
                    countryId = p.PrimaryTown != null ? p.PrimaryTown.CountryId : (int?)null,

                    highestCourtAllowedId = p.HighestCourtAllowedId,
                    highestCourtAllowedName = p.HighestCourtAllowed != null ? p.HighestCourtAllowed.Name : null,

                    googlePlaceId = p.GooglePlaceId,
                    googleFormattedAddress = p.GoogleFormattedAddress,
                    latitude = p.Latitude,
                    longitude = p.Longitude,

                    verificationStatus = p.VerificationStatus.ToString(),
                    isActive = p.IsActive,

                    townIdsServed = p.TownsServed.Select(t => t.TownId).Distinct().ToList(),
                    practiceAreaIds = p.PracticeAreas.Select(a => a.PracticeAreaId).Distinct().ToList(),

                    // ✅ NEW: services + rate cards
                    serviceOfferings = p.ServiceOfferings
                        .OrderBy(s => s.LawyerServiceId)
                        .Select(s => new
                        {
                            lawyerServiceId = s.LawyerServiceId,
                            name = s.LawyerService != null ? s.LawyerService.Name : "",
                            currency = s.Currency,
                            minFee = s.MinFee,
                            maxFee = s.MaxFee,
                            unit = s.Unit.ToString(),
                            notes = s.Notes
                        })
                        .ToList()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load your lawyer profile.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // APPLY / UPDATE my lawyer profile
        // POST /api/lawyers/me
        // -------------------------
        [HttpPost]
        public async Task<IActionResult> UpsertMine([FromBody] LawyerApplyDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            try
            {
                var userId = User.GetUserId();

                // ✅ Validate PrimaryTown (anchor country)
                var primaryTown = await _db.Towns
                    .AsNoTracking()
                    .Where(t => t.Id == dto.PrimaryTownId)
                    .Select(t => new { t.Id, t.CountryId })
                    .FirstOrDefaultAsync(ct);

                if (primaryTown == null)
                    return BadRequest(new { message = "PrimaryTownId is invalid." });

                var countryId = primaryTown.CountryId;

                // ✅ Validate HighestCourtAllowed (if provided) matches country
                if (dto.HighestCourtAllowedId.HasValue)
                {
                    var courtOk = await _db.Courts
                        .AsNoTracking()
                        .AnyAsync(c => c.Id == dto.HighestCourtAllowedId.Value && c.CountryId == countryId && c.IsActive, ct);

                    if (!courtOk)
                        return BadRequest(new { message = "HighestCourtAllowedId is invalid or does not match selected country." });
                }

                // ✅ Validate towns served list (must exist, same country)
                var servedTownIds = (dto.TownIdsServed ?? new List<int>())
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

                if (!servedTownIds.Contains(dto.PrimaryTownId))
                    servedTownIds.Add(dto.PrimaryTownId);

                if (servedTownIds.Count > 0)
                {
                    var servedCount = await _db.Towns
                        .AsNoTracking()
                        .CountAsync(t => servedTownIds.Contains(t.Id) && t.CountryId == countryId, ct);

                    if (servedCount != servedTownIds.Count)
                        return BadRequest(new { message = "One or more TownIdsServed are invalid or do not match selected country." });
                }

                // ✅ Validate practice areas list
                var practiceAreaIds = (dto.PracticeAreaIds ?? new List<int>())
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

                if (practiceAreaIds.Count > 0)
                {
                    var paCount = await _db.PracticeAreas
                        .AsNoTracking()
                        .CountAsync(p => practiceAreaIds.Contains(p.Id) && p.IsActive, ct);

                    if (paCount != practiceAreaIds.Count)
                        return BadRequest(new { message = "One or more PracticeAreaIds are invalid." });
                }

                // ✅ NEW: validate services list (master list, active)
                var services = (dto.Services ?? new List<LawyerServiceOfferingUpsertDto>())
                    .Where(x => x.LawyerServiceId > 0)
                    .GroupBy(x => x.LawyerServiceId)
                    .Select(g => g.First())
                    .ToList();

                if (services.Count > 0)
                {
                    var serviceIds = services.Select(x => x.LawyerServiceId).ToList();
                    var count = await _db.LawyerServices
                        .AsNoTracking()
                        .CountAsync(s => serviceIds.Contains(s.Id) && s.IsActive, ct);

                    if (count != serviceIds.Count)
                        return BadRequest(new { message = "One or more selected services are invalid." });
                }

                // ✅ Upsert profile (include offerings)
                var p = await _db.LawyerProfiles
                    .Include(x => x.TownsServed)
                    .Include(x => x.PracticeAreas)
                    .Include(x => x.ServiceOfferings)
                    .FirstOrDefaultAsync(x => x.UserId == userId, ct);

                var now = DateTime.UtcNow;

                if (p == null)
                {
                    p = new LawyerProfile
                    {
                        UserId = userId,
                        VerificationStatus = LawyerVerificationStatus.Pending,
                        IsActive = true,
                        CreatedAt = now
                    };
                    _db.LawyerProfiles.Add(p);
                }
                else
                {
                    p.UpdatedAt = now;

                    if (p.VerificationStatus == LawyerVerificationStatus.Rejected ||
                        p.VerificationStatus == LawyerVerificationStatus.Suspended)
                    {
                        p.VerificationStatus = LawyerVerificationStatus.Pending;
                    }
                }

                // Scalar fields
                p.DisplayName = (dto.DisplayName ?? "").Trim();
                p.FirmName = string.IsNullOrWhiteSpace(dto.FirmName) ? null : dto.FirmName.Trim();
                p.Bio = string.IsNullOrWhiteSpace(dto.Bio) ? null : dto.Bio.Trim();
                p.PrimaryPhone = string.IsNullOrWhiteSpace(dto.PrimaryPhone) ? null : dto.PrimaryPhone.Trim();
                p.PublicEmail = string.IsNullOrWhiteSpace(dto.PublicEmail) ? null : dto.PublicEmail.Trim();

                p.PrimaryTownId = dto.PrimaryTownId;
                p.HighestCourtAllowedId = dto.HighestCourtAllowedId;

                p.GooglePlaceId = string.IsNullOrWhiteSpace(dto.GooglePlaceId) ? null : dto.GooglePlaceId.Trim();
                p.GoogleFormattedAddress = string.IsNullOrWhiteSpace(dto.GoogleFormattedAddress) ? null : dto.GoogleFormattedAddress.Trim();
                p.Latitude = dto.Latitude;
                p.Longitude = dto.Longitude;

                // Replace joins: towns served
                p.TownsServed.Clear();
                foreach (var tid in servedTownIds)
                    p.TownsServed.Add(new LawyerTown { TownId = tid, IsOfficeLocation = (tid == dto.PrimaryTownId) });

                // Replace joins: practice areas
                p.PracticeAreas.Clear();
                foreach (var pid in practiceAreaIds)
                    p.PracticeAreas.Add(new LawyerPracticeArea { PracticeAreaId = pid });

                // ✅ Replace joins: services + rate cards
                p.ServiceOfferings.Clear();
                foreach (var s in services)
                {
                    var currency = (s.Currency ?? "KES").Trim().ToUpperInvariant();
                    if (currency.Length > 10) currency = currency.Substring(0, 10);
                    if (string.IsNullOrWhiteSpace(currency)) currency = "KES";

                    p.ServiceOfferings.Add(new LawyerServiceOffering
                    {
                        LawyerServiceId = s.LawyerServiceId,
                        Currency = currency,
                        MinFee = s.MinFee,
                        MaxFee = s.MaxFee,
                        Unit = s.Unit,
                        Notes = string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes.Trim(),
                        CreatedAt = now
                    });
                }

                await _db.SaveChangesAsync(ct);

                return Ok(new
                {
                    message = "Lawyer profile submitted successfully.",
                    lawyerProfileId = p.Id,
                    verificationStatus = p.VerificationStatus.ToString()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to submit lawyer profile.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }
    }
}