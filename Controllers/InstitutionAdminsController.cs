using LawAfrica.API.Data;
using LawAfrica.API.Models.DTOs.Institutions;
using LawAfrica.API.Models.Institutions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/institution-admins")]
    [Authorize(Roles = "Admin")]
    public class InstitutionAdminsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public InstitutionAdminsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            var data = await _db.InstitutionMemberships
                .AsNoTracking()
                .Where(m => m.MemberType == InstitutionMemberType.InstitutionAdmin)
                .Include(m => m.Institution)
                .Include(m => m.User)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new InstitutionAdminListItemDto
                {
                    Id = m.Id,
                    InstitutionId = m.InstitutionId,
                    InstitutionName = m.Institution.Name,
                    UserId = m.UserId,
                    UserEmail = m.User.Email ?? m.User.Username ?? "",
                    Role = "InstitutionAdmin",
                    IsActive = m.IsActive,
                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt
                })
                .ToListAsync(ct);

            return Ok(data);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var m = await _db.InstitutionMemberships
                .AsNoTracking()
                .Where(x => x.Id == id && x.MemberType == InstitutionMemberType.InstitutionAdmin)
                .Include(x => x.Institution)
                .Include(x => x.User)
                .Select(x => new InstitutionAdminListItemDto
                {
                    Id = x.Id,
                    InstitutionId = x.InstitutionId,
                    InstitutionName = x.Institution.Name,
                    UserId = x.UserId,
                    UserEmail = x.User.Email ?? x.User.Username ?? "",
                    Role = "InstitutionAdmin",
                    IsActive = x.IsActive,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                })
                .FirstOrDefaultAsync(ct);

            if (m == null) return NotFound("Institution admin assignment not found.");
            return Ok(m);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertInstitutionAdminRequest req, CancellationToken ct)
        {
            if (req.InstitutionId <= 0) return BadRequest("InstitutionId is required.");
            if (string.IsNullOrWhiteSpace(req.UserEmail)) return BadRequest("UserEmail is required.");

            var email = req.UserEmail.Trim().ToLowerInvariant();

            var institution = await _db.Institutions.FirstOrDefaultAsync(i => i.Id == req.InstitutionId, ct);
            if (institution == null) return BadRequest("Institution not found.");

            var user = await _db.Users.FirstOrDefaultAsync(u => (u.Email ?? "").ToLower() == email, ct);
            if (user == null) return BadRequest("User not found by email.");

            // Upsert by (InstitutionId, UserId)
            var existing = await _db.InstitutionMemberships
                .FirstOrDefaultAsync(m => m.InstitutionId == req.InstitutionId && m.UserId == user.Id, ct);

            if (existing == null)
            {
                existing = new InstitutionMembership
                {
                    InstitutionId = req.InstitutionId,
                    UserId = user.Id,
                    MemberType = InstitutionMemberType.InstitutionAdmin,
                    ReferenceNumber = "ADMIN",
                    Status = MembershipStatus.Approved,
                    IsActive = req.IsActive,
                    ApprovedByUserId = null,
                    ApprovedAt = DateTime.UtcNow,
                    AdminNotes = "Assigned as Institution Admin by Global Admin.",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = null
                };

                _db.InstitutionMemberships.Add(existing);
            }
            else
            {
                existing.MemberType = InstitutionMemberType.InstitutionAdmin;
                existing.Status = MembershipStatus.Approved;
                existing.IsActive = req.IsActive;
                existing.ReferenceNumber = string.IsNullOrWhiteSpace(existing.ReferenceNumber) ? "ADMIN" : existing.ReferenceNumber;
                existing.AdminNotes = string.IsNullOrWhiteSpace(existing.AdminNotes)
                    ? "Promoted to Institution Admin by Global Admin."
                    : existing.AdminNotes;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new { id = existing.Id });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpsertInstitutionAdminRequest req, CancellationToken ct)
        {
            if (req.InstitutionId <= 0) return BadRequest("InstitutionId is required.");
            if (string.IsNullOrWhiteSpace(req.UserEmail)) return BadRequest("UserEmail is required.");

            var m = await _db.InstitutionMemberships.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m == null) return NotFound("Institution admin assignment not found.");

            // Force semantics: this controller manages InstitutionAdmin memberships only
            m.MemberType = InstitutionMemberType.InstitutionAdmin;
            m.Status = MembershipStatus.Approved;

            if (m.InstitutionId != req.InstitutionId)
                m.InstitutionId = req.InstitutionId;

            var email = req.UserEmail.Trim().ToLowerInvariant();
            var user = await _db.Users.FirstOrDefaultAsync(u => (u.Email ?? "").ToLower() == email, ct);
            if (user == null) return BadRequest("User not found by email.");

            m.UserId = user.Id;
            m.IsActive = req.IsActive;
            m.ReferenceNumber = string.IsNullOrWhiteSpace(m.ReferenceNumber) ? "ADMIN" : m.ReferenceNumber;
            m.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return Ok(new { ok = true });
        }

        [HttpPost("{id:int}/activate")]
        public async Task<IActionResult> Activate(int id, CancellationToken ct)
        {
            var m = await _db.InstitutionMemberships.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m == null) return NotFound("Institution admin assignment not found.");

            m.MemberType = InstitutionMemberType.InstitutionAdmin;
            m.Status = MembershipStatus.Approved;
            m.IsActive = true;
            m.ReferenceNumber = string.IsNullOrWhiteSpace(m.ReferenceNumber) ? "ADMIN" : m.ReferenceNumber;
            m.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return Ok(new { ok = true });
        }

        [HttpPost("{id:int}/deactivate")]
        public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
        {
            var m = await _db.InstitutionMemberships.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m == null) return NotFound("Institution admin assignment not found.");

            m.IsActive = false;
            m.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return Ok(new { ok = true });
        }
    }
}
