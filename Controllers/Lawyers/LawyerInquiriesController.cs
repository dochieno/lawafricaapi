// =======================================================
// FILE: LawAfrica.API/Controllers/Lawyers/LawyerInquiriesController.cs
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

        // ==========================================================
        // ✅ NEW ENDPOINTS FOR "MY INQUIRIES" WORKFLOW
        // ==========================================================

        // -------------------------
        // Inquiry DETAIL (requester OR assigned lawyer)
        // GET /api/lawyers/inquiries/{id}
        // -------------------------
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken ct)
        {
            try
            {
                var userId = User.GetUserId();

                var x = await _inquiries.GetInquiryDetailAsync(id, userId, ct);

                if (x == null)
                    return NotFound(new { message = "Inquiry not found." });

                return Ok(x);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load inquiry detail.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // Update status (lawyer-side; requester can close only)
        // PATCH /api/lawyers/inquiries/{id}/status
        // body: { status: "Contacted" | "InProgress" | "Closed" | "Spam", outcome?: "Resolved"... , note? }
        // -------------------------
        [HttpPatch("{id:int}/status")]
        public async Task<IActionResult> PatchStatus(
            [FromRoute] int id,
            [FromBody] UpdateInquiryStatusRequestDto dto,
            CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            try
            {
                var userId = User.GetUserId();

                if (!Enum.TryParse<InquiryStatus>(dto.Status, ignoreCase: true, out var status))
                    return BadRequest(new { message = "Invalid status." });

                InquiryOutcome? outcome = null;
                if (!string.IsNullOrWhiteSpace(dto.Outcome))
                {
                    if (!Enum.TryParse<InquiryOutcome>(dto.Outcome, ignoreCase: true, out var parsedOutcome))
                        return BadRequest(new { message = "Invalid outcome." });

                    outcome = parsedOutcome;
                }

                var updated = await _inquiries.UpdateStatusAsync(
                    inquiryId: id,
                    actorUserId: userId,
                    status: status,
                    outcome: outcome,
                    note: dto.Note,
                    ct: ct);

                return Ok(new
                {
                    id = updated.Id,
                    status = updated.Status.ToString(),
                    outcome = updated.Outcome?.ToString(),
                    updatedAt = updated.UpdatedAt
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = "Inquiry not found." });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to update inquiry status.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // Close inquiry (shortcut)
        // POST /api/lawyers/inquiries/{id}/close
        // body: { outcome: "Resolved" | "NotResolved" | "NoResponse" | "Declined" | "Duplicate", note? }
        // -------------------------
        [HttpPost("{id:int}/close")]
        public async Task<IActionResult> Close(
            [FromRoute] int id,
            [FromBody] CloseInquiryRequestDto dto,
            CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            try
            {
                var userId = User.GetUserId();

                if (!Enum.TryParse<InquiryOutcome>(dto.Outcome, ignoreCase: true, out var outcome))
                    return BadRequest(new { message = "Invalid outcome." });

                var updated = await _inquiries.CloseAsync(
                    inquiryId: id,
                    actorUserId: userId,
                    outcome: outcome,
                    note: dto.Note,
                    ct: ct);

                return Ok(new
                {
                    id = updated.Id,
                    status = updated.Status.ToString(),
                    outcome = updated.Outcome?.ToString(),
                    closedAt = updated.ClosedAtUtc
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = "Inquiry not found." });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to close inquiry.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // Rating (requester only; after Closed)
        // POST /api/lawyers/inquiries/{id}/rating
        // body: { stars: 1..5, comment? }
        // -------------------------
        [HttpPost("{id:int}/rating")]
        public async Task<IActionResult> Rate(
            [FromRoute] int id,
            [FromBody] CreateInquiryRatingRequestDto dto,
            CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            try
            {
                var userId = User.GetUserId();

                var rated = await _inquiries.RateAsync(
                    inquiryId: id,
                    requesterUserId: userId,
                    stars: dto.Stars,
                    comment: dto.Comment,
                    ct: ct);

                return Ok(new
                {
                    id = rated.Id,
                    ratingStars = rated.RatingStars,
                    ratingComment = rated.RatingComment,
                    ratedAt = rated.RatedAtUtc
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = "Inquiry not found." });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to rate inquiry.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }
    }
}