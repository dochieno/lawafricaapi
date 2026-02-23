// FILE: LawAfrica.API/Controllers/Admin/AdminLawyerProfilesController.cs
using LawAfrica.API.Data;
using LawAfrica.API.Models.Lawyers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/lawyers/profiles")]
    [Authorize(Roles = "Admin")]
    public class AdminLawyerProfilesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AdminLawyerProfilesController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET /api/admin/lawyers/profiles?q=&status=&take=&skip=
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? q = null,
            [FromQuery] string? status = null,
            [FromQuery] int take = 50,
            [FromQuery] int skip = 0,
            CancellationToken ct = default)
        {
            q = (q ?? "").Trim();
            take = Math.Clamp(take, 1, 200);
            skip = Math.Max(0, skip);

            var query = _db.LawyerProfiles
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.PrimaryTown).ThenInclude(t => t.Country)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<LawyerVerificationStatus>(status, true, out var st))
            {
                query = query.Where(x => x.VerificationStatus == st);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(x =>
                    x.DisplayName.Contains(q) ||
                    (x.FirmName != null && x.FirmName.Contains(q)) ||
                    (x.User != null && (x.User.Email.Contains(q) || x.User.Username.Contains(q))));
            }

            var total = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(x => new
                {
                    id = x.Id,
                    userId = x.UserId,
                    displayName = x.DisplayName,
                    firmName = x.FirmName,
                    verificationStatus = x.VerificationStatus.ToString(),
                    isActive = x.IsActive,
                    createdAt = x.CreatedAt,
                    updatedAt = x.UpdatedAt,

                    userEmail = x.User != null ? x.User.Email : null,
                    userPhone = x.User != null ? x.User.PhoneNumber : null,

                    countryName = x.PrimaryTown != null && x.PrimaryTown.Country != null ? x.PrimaryTown.Country.Name : null,
                    primaryTownName = x.PrimaryTown != null ? x.PrimaryTown.Name : null
                })
                .ToListAsync(ct);

            return Ok(new { total, take, skip, items });
        }

        // GET /api/admin/lawyers/profiles/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var p = await _db.LawyerProfiles
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.PrimaryTown).ThenInclude(t => t.Country)
                .Include(x => x.HighestCourtAllowed)
                .Include(x => x.PracticeAreas).ThenInclude(pa => pa.PracticeArea)
                .Include(x => x.TownsServed).ThenInclude(ts => ts.Town)
                .Include(x => x.ServiceOfferings).ThenInclude(s => s.LawyerService)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (p == null) return NotFound(new { message = "Lawyer profile not found." });

            var docs = await _db.LawyerProfileDocuments
                .AsNoTracking()
                .Where(d => d.LawyerProfileId == id)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new
                {
                    id = d.Id,
                    type = d.Type.ToString(),
                    typeId = (short)d.Type,
                    fileName = d.FileName,
                    contentType = d.ContentType,
                    sizeBytes = d.SizeBytes,
                    urlPath = d.UrlPath,
                    createdAt = d.CreatedAt
                })
                .ToListAsync(ct);

            return Ok(new
            {
                id = p.Id,
                userId = p.UserId,
                displayName = p.DisplayName,
                firmName = p.FirmName,
                bio = p.Bio,
                primaryPhone = p.PrimaryPhone,
                publicEmail = p.PublicEmail,

                verificationStatus = p.VerificationStatus.ToString(),
                isActive = p.IsActive,
                createdAt = p.CreatedAt,
                updatedAt = p.UpdatedAt,

                userEmail = p.User != null ? p.User.Email : null,
                userPhone = p.User != null ? p.User.PhoneNumber : null,
                userUsername = p.User != null ? p.User.Username : null,

                countryName = p.PrimaryTown != null && p.PrimaryTown.Country != null ? p.PrimaryTown.Country.Name : null,
                primaryTownName = p.PrimaryTown != null ? p.PrimaryTown.Name : null,

                highestCourtAllowedName = p.HighestCourtAllowed != null ? p.HighestCourtAllowed.Name : null,

                practiceAreas = p.PracticeAreas.Select(x => x.PracticeArea.Name).Distinct().ToList(),
                townsServed = p.TownsServed.Select(x => x.Town.Name).Distinct().ToList(),

                serviceOfferings = p.ServiceOfferings.Select(x => new
                {
                    lawyerServiceId = x.LawyerServiceId,
                    serviceName = x.LawyerService.Name,
                    currency = x.Currency,
                    minFee = x.MinFee,
                    maxFee = x.MaxFee,
                    billingUnit = x.BillingUnit,
                    notes = x.Notes
                }).ToList(),

                documents = docs
            });
        }

        public class VerifyDto
        {
            public string Action { get; set; } = "verify"; // verify|reject|suspend
            public string? Reason { get; set; }
        }

        // POST /api/admin/lawyers/profiles/{id}/verify
        [HttpPost("{id:int}/verify")]
        public async Task<IActionResult> Verify(int id, [FromBody] VerifyDto dto, CancellationToken ct)
        {
            var p = await _db.LawyerProfiles.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p == null) return NotFound(new { message = "Lawyer profile not found." });

            var action = (dto.Action ?? "").Trim().ToLowerInvariant();

            if (action == "verify")
            {
                p.VerificationStatus = LawyerVerificationStatus.Verified;
                p.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return Ok(new { message = "Profile verified." });
            }

            if (action == "reject")
            {
                p.VerificationStatus = LawyerVerificationStatus.Rejected;
                // Optional: add RejectionReason field later if you want it persisted
                p.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return Ok(new { message = "Profile rejected.", reason = dto.Reason });
            }

            if (action == "suspend")
            {
                p.VerificationStatus = LawyerVerificationStatus.Suspended;
                p.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return Ok(new { message = "Profile suspended.", reason = dto.Reason });
            }

            return BadRequest(new { message = "Action must be verify|reject|suspend." });
        }
    }
}