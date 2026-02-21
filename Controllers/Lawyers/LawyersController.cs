// =======================================================
// FILE: LawAfrica.API/Controllers/Lawyers/LawyersController.cs
// =======================================================
using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Lawyers;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models.Lawyers;
using LawAfrica.API.Services.Lawyers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/lawyers")]
    [Authorize] // ✅ everything here requires login
    public class LawyersController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILawyerDirectoryService _directory;

        public LawyersController(
            ApplicationDbContext db,
            ILawyerDirectoryService directory)
        {
            _db = db;
            _directory = directory;
        }

        // -------------------------
        // GET single lawyer profile
        // GET /api/lawyers/{id}
        // -------------------------
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            try
            {
                // Logged-in required (also helps ensure consistent behavior)
                var userId = User.GetUserId();

                var x = await _directory.GetLawyerAsync(id, ct);
                if (x == null) return NotFound(new { message = "Lawyer not found." });

                var dto = new LawyerDetailDto
                {
                    Id = x.Id,
                    DisplayName = x.DisplayName,
                    FirmName = x.FirmName,
                    Bio = x.Bio,

                    IsVerified = x.VerificationStatus == LawyerVerificationStatus.Verified,
                    HighestCourtName = x.HighestCourtAllowed?.Name,

                    PrimaryPhone = x.PrimaryPhone,
                    PublicEmail = x.PublicEmail,

                    PrimaryTownId = x.PrimaryTownId,
                    PrimaryTownName = x.PrimaryTown?.Name,
                    CountryName = x.PrimaryTown?.Country?.Name,

                    PracticeAreas = x.PracticeAreas.Select(p => p.PracticeArea.Name).Distinct().ToList(),
                    TownsServed = x.TownsServed.Select(t => t.Town.Name).Distinct().ToList(),

                    GoogleFormattedAddress = x.GoogleFormattedAddress,
                    Latitude = x.Latitude,
                    Longitude = x.Longitude,

                    ProfileImageUrl = x.User?.ProfileImageUrl
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load lawyer profile.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // SEARCH lawyers
        // GET /api/lawyers/search?... (query params)
        // -------------------------
        [HttpGet("search")]
        public async Task<IActionResult> Search(
            [FromQuery] int? countryId = null,
            [FromQuery] int? townId = null,
            [FromQuery] int? practiceAreaId = null,
            [FromQuery] int? highestCourtAllowedId = null,
            [FromQuery] bool verifiedOnly = true,
            [FromQuery] string? q = null,
            [FromQuery] int take = 30,
            [FromQuery] int skip = 0,
            CancellationToken ct = default)
        {
            q = (q ?? "").Trim();

            if (take <= 0) take = 30;
            if (take > 60) take = 60;
            if (skip < 0) skip = 0;

            try
            {
                var userId = User.GetUserId();

                var list = await _directory.SearchLawyersAsync(
                    countryId,
                    townId,
                    practiceAreaId,
                    highestCourtAllowedId,
                    verifiedOnly,
                    q,
                    take,
                    skip,
                    ct);

                var items = list.Select(x => new LawyerListItemDto
                {
                    Id = x.Id,
                    DisplayName = x.DisplayName,
                    FirmName = x.FirmName,
                    PrimaryTownId = x.PrimaryTownId,
                    PrimaryTownName = x.PrimaryTown?.Name,
                    CountryName = x.PrimaryTown?.Country?.Name,
                    IsVerified = x.VerificationStatus == LawyerVerificationStatus.Verified,
                    HighestCourtName = x.HighestCourtAllowed?.Name,
                    ProfileImageUrl = x.User?.ProfileImageUrl
                }).ToList();

                return Ok(new
                {
                    items,
                    take,
                    skip
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to search lawyers.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // LOOKUP: Practice Areas (for dropdown)
        // GET /api/lawyers/practice-areas?q=family
        // -------------------------
        [HttpGet("practice-areas")]
        public async Task<IActionResult> PracticeAreasLookup(
            [FromQuery] string? q = null,
            [FromQuery] int take = 200,
            CancellationToken ct = default)
        {
            q = (q ?? "").Trim();
            take = Math.Clamp(take, 1, 500);

            try
            {
                var userId = User.GetUserId();

                var query = _db.PracticeAreas
                    .AsNoTracking()
                    .Where(x => x.IsActive);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    query = query.Where(x =>
                        x.Name.Contains(q) ||
                        (x.Slug != null && x.Slug.Contains(q)));
                }

                var items = await query
                    .OrderBy(x => x.Name)
                    .Take(take)
                    .Select(x => new
                    {
                        id = x.Id,
                        name = x.Name
                    })
                    .ToListAsync(ct);

                return Ok(items);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load practice areas.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // LOOKUP: Towns (for dropdown)
        // GET /api/lawyers/towns?countryId=1&q=nai&take=200
        // NOTE: convenience alias to avoid frontend calling /api/towns
        // -------------------------
        [HttpGet("towns")]
        public async Task<IActionResult> TownsLookup(
            [FromQuery] int countryId,
            [FromQuery] string? q = null,
            [FromQuery] int take = 200,
            CancellationToken ct = default)
        {
            if (countryId <= 0)
                return BadRequest(new { message = "countryId is required." });

            q = (q ?? "").Trim();
            take = Math.Clamp(take, 1, 500);

            try
            {
                var userId = User.GetUserId();

                var query = _db.Towns
                    .AsNoTracking()
                    .Where(x => x.CountryId == countryId);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    query = query.Where(x =>
                        x.Name.Contains(q) ||
                        x.PostCode.Contains(q));
                }

                var items = await query
                    .OrderBy(x => x.Name)
                    .ThenBy(x => x.PostCode)
                    .Take(take)
                    .Select(x => new
                    {
                        id = x.Id,
                        name = x.Name,
                        postCode = x.PostCode
                    })
                    .ToListAsync(ct);

                return Ok(items);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load towns.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // LOOKUP: Courts (for dropdown)
        // GET /api/lawyers/courts?countryId=1&q=high&take=200
        // NOTE: public lookup for logged-in users (CourtsController is admin-only)
        // -------------------------
        [HttpGet("courts")]
        public async Task<IActionResult> CourtsLookup(
            [FromQuery] int countryId,
            [FromQuery] string? q = null,
            [FromQuery] int take = 200,
            CancellationToken ct = default)
        {
            if (countryId <= 0)
                return BadRequest(new { message = "countryId is required." });

            q = (q ?? "").Trim();
            take = Math.Clamp(take, 1, 500);

            try
            {
                var userId = User.GetUserId();

                var query = _db.Courts
                    .AsNoTracking()
                    .Where(c => c.CountryId == countryId && c.IsActive);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    query = query.Where(c =>
                        (c.Name != null && c.Name.Contains(q)) ||
                        (c.Code != null && c.Code.Contains(q)) ||
                        (c.Abbreviation != null && c.Abbreviation.Contains(q)));
                }

                var items = await query
                    .OrderBy(c => c.Name)
                    .Take(take)
                    .Select(c => new
                    {
                        id = c.Id,
                        name = c.Name,
                        code = c.Code
                    })
                    .ToListAsync(ct);

                return Ok(items);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load courts.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }
    }
}