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
    }
}