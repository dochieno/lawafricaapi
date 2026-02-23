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

                    // ✅ service offerings + rate card
                    serviceOfferings = p.ServiceOfferings
                        .OrderBy(x => x.LawyerService != null ? x.LawyerService.Name : "")
                        .Select(x => new
                        {
                            lawyerServiceId = x.LawyerServiceId,
                            serviceName = x.LawyerService != null ? x.LawyerService.Name : null,
                            currency = x.Currency,
                            minFee = x.MinFee,
                            maxFee = x.MaxFee,
                            billingUnit = x.BillingUnit,
                            notes = x.Notes
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

                // Ensure primary town is included
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

                // ✅ Accept BOTH dto.ServiceOfferings (canonical) and dto.Services (legacy/back-compat)
                var offeringsSrc =
                    (dto.ServiceOfferings != null && dto.ServiceOfferings.Count > 0)
                        ? dto.ServiceOfferings
                        : (dto.Services ?? new List<LawyerServiceOfferingUpsertDto>());

                var offerings = (offeringsSrc ?? new List<LawyerServiceOfferingUpsertDto>())
                    .Where(x => x != null && x.LawyerServiceId > 0)
                    .GroupBy(x => x.LawyerServiceId)
                    .Select(g => g.First())
                    .ToList();

                // ✅ Validate services exist + basic numeric checks
                if (offerings.Count > 0)
                {
                    var ids = offerings.Select(x => x.LawyerServiceId).Distinct().ToList();

                    var svcCount = await _db.LawyerServices
                        .AsNoTracking()
                        .CountAsync(s => ids.Contains(s.Id) && s.IsActive, ct);

                    if (svcCount != ids.Count)
                        return BadRequest(new { message = "One or more selected Services are invalid/inactive." });

                    foreach (var o in offerings)
                    {
                        var cur = (o.Currency ?? "").Trim();
                        if (cur.Length > 0 && cur.Length > 10)
                            return BadRequest(new { message = "Currency must be <= 10 chars." });

                        if (o.MinFee.HasValue && o.MinFee.Value < 0)
                            return BadRequest(new { message = "MinFee cannot be negative." });

                        if (o.MaxFee.HasValue && o.MaxFee.Value < 0)
                            return BadRequest(new { message = "MaxFee cannot be negative." });

                        if (o.MinFee.HasValue && o.MaxFee.HasValue && o.MinFee.Value > o.MaxFee.Value)
                            return BadRequest(new { message = "MinFee cannot be greater than MaxFee." });
                    }
                }

                // ✅ Upsert profile
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

                    // If previously rejected/suspended, re-apply goes back to Pending
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

                // Replace joins (clear then add)
                p.TownsServed.Clear();
                foreach (var tid in servedTownIds)
                {
                    p.TownsServed.Add(new LawyerTown
                    {
                        TownId = tid,
                        IsOfficeLocation = (tid == dto.PrimaryTownId)
                    });
                }

                p.PracticeAreas.Clear();
                foreach (var pid in practiceAreaIds)
                {
                    p.PracticeAreas.Add(new LawyerPracticeArea
                    {
                        PracticeAreaId = pid
                    });
                }

                // ✅ Replace service offerings (FIXED: sets FK + safe Currency)
                p.ServiceOfferings.Clear();
                foreach (var o in offerings)
                {
                    var currency = string.IsNullOrWhiteSpace(o.Currency) ? "KES" : o.Currency.Trim();

                    p.ServiceOfferings.Add(new LawyerServiceOffering
                    {
                        // ✅ IMPORTANT: ensures LawyerProfileId is set for composite key
                        LawyerProfile = p,

                        LawyerServiceId = o.LawyerServiceId,
                        Currency = currency,

                        MinFee = o.MinFee,
                        MaxFee = o.MaxFee,

                        Unit = o.Unit,
                        BillingUnit = string.IsNullOrWhiteSpace(o.BillingUnit) ? null : o.BillingUnit.Trim(),
                        Notes = string.IsNullOrWhiteSpace(o.Notes) ? null : o.Notes.Trim(),

                        UpdatedAt = now
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