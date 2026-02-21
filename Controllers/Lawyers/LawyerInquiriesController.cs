// =======================================================
// FILE: LawAfrica.API/Controllers/Lawyers/LawyerInquiriesController.cs
// =======================================================
using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Lawyers;
using LawAfrica.API.Helpers;
using LawAfrica.API.Services.Lawyers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/lawyers/inquiries")]
    [Authorize] // ✅ everything here requires login
    public class LawyerInquiriesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILawyerInquiryService _inquiries;

        public LawyerInquiriesController(
            ApplicationDbContext db,
            ILawyerInquiryService inquiries)
        {
            _db = db;
            _inquiries = inquiries;
        }

        // -------------------------
        // CREATE inquiry (client -> lawyer or general)
        // POST /api/lawyers/inquiries
        // -------------------------
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateInquiryRequestDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            try
            {
                var userId = User.GetUserId();

                var created = await _inquiries.CreateInquiryAsync(
                    requesterUserId: userId,
                    lawyerProfileId: dto.LawyerProfileId,
                    practiceAreaId: dto.PracticeAreaId,
                    townId: dto.TownId,
                    problemSummary: dto.ProblemSummary,
                    preferredContactMethod: dto.PreferredContactMethod,
                    ct: ct);

                return Ok(new
                {
                    id = created.Id,
                    status = created.Status.ToString(),
                    createdAt = created.CreatedAt
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to create inquiry.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // MY inquiries (client side)
        // GET /api/lawyers/inquiries/mine?take=50&skip=0
        // -------------------------
        [HttpGet("mine")]
        public async Task<IActionResult> Mine(
            [FromQuery] int take = 50,
            [FromQuery] int skip = 0,
            CancellationToken ct = default)
        {
            if (take <= 0) take = 50;
            if (take > 100) take = 100;
            if (skip < 0) skip = 0;

            try
            {
                var userId = User.GetUserId();

                var list = await _inquiries.GetMyInquiriesAsync(userId, take, skip, ct);

                var items = list.Select(x => new InquiryDto
                {
                    Id = x.Id,
                    LawyerProfileId = x.LawyerProfileId,
                    RequesterUserId = x.RequesterUserId,
                    ProblemSummary = x.ProblemSummary,
                    Status = x.Status.ToString(),
                    CreatedAt = x.CreatedAt,
                    PracticeAreaName = x.PracticeArea?.Name,
                    TownName = x.Town?.Name
                }).ToList();

                return Ok(new { items, take, skip });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load your inquiries.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // INQUIRIES FOR ME (lawyer side)
        // GET /api/lawyers/inquiries/for-me?take=50&skip=0
        // -------------------------
        [HttpGet("for-me")]
        public async Task<IActionResult> ForMe(
            [FromQuery] int take = 50,
            [FromQuery] int skip = 0,
            CancellationToken ct = default)
        {
            if (take <= 0) take = 50;
            if (take > 100) take = 100;
            if (skip < 0) skip = 0;

            try
            {
                var userId = User.GetUserId();

                var list = await _inquiries.GetLawyerInquiriesAsync(userId, take, skip, ct);

                var items = list.Select(x => new InquiryDto
                {
                    Id = x.Id,
                    LawyerProfileId = x.LawyerProfileId,
                    RequesterUserId = x.RequesterUserId,
                    ProblemSummary = x.ProblemSummary,
                    Status = x.Status.ToString(),
                    CreatedAt = x.CreatedAt,
                    PracticeAreaName = x.PracticeArea?.Name,
                    TownName = x.Town?.Name,
                    RequesterName = x.RequesterUser != null
                        ? $"{(x.RequesterUser.FirstName ?? "").Trim()} {(x.RequesterUser.LastName ?? "").Trim()}".Trim()
                        : null,
                    RequesterPhone = x.RequesterUser?.PhoneNumber,
                    RequesterEmail = x.RequesterUser?.Email
                }).ToList();

                return Ok(new { items, take, skip });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load inquiries for this lawyer.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }
    }
}