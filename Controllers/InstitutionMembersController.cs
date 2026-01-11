using LawAfrica.API.Authorization.Policies;
using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models.DTOs.Institutions;
using LawAfrica.API.Models.Institutions;
using LawAfrica.API.Services.Institutions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/institutions/{institutionId}/members")]
    [Authorize(Policy = PolicyNames.IsInstitutionAdmin)]
    public class InstitutionMembersController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly InstitutionSeatService _seatService;

        public InstitutionMembersController(ApplicationDbContext db, InstitutionSeatService seatService)
        {
            _db = db;
            _seatService = seatService;
        }

        // 1.0 Get Institution Seats
        [HttpGet("seats")]
        public async Task<IActionResult> GetSeatUsage(int institutionId)
        {
            // Students
            var studentSeats = await _seatService.GetSeatUsageAsync(institutionId, InstitutionMemberType.Student);

            // Staff bucket includes InstitutionAdmin, so we present combined usage for UI
            var staffSeats = await _seatService.GetSeatUsageAsync(institutionId, InstitutionMemberType.Staff);
            var adminSeats = await _seatService.GetSeatUsageAsync(institutionId, InstitutionMemberType.InstitutionAdmin);

            return Ok(new
            {
                students = new { used = studentSeats.used, max = studentSeats.max },
                staff = new { used = staffSeats.used + adminSeats.used, max = staffSeats.max }
            });
        }

        // 0.9 List pending member requests (shape expected by InstitutionApprovalDashboard.jsx)
        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingMembers(int institutionId)
        {
            var pending = await _db.InstitutionMemberships
                .AsNoTracking()
                .Include(m => m.User)
                .Where(m => m.InstitutionId == institutionId && m.Status == MembershipStatus.PendingApproval)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    id = m.Id, // ✅ frontend uses m.id
                    institutionId = m.InstitutionId,
                    userId = m.UserId,
                    username = m.User.Username,
                    email = m.User.Email,
                    memberType = m.MemberType.ToString(),
                    referenceNumber = m.ReferenceNumber,
                    createdAt = m.CreatedAt
                })
                .ToListAsync();

            return Ok(pending);
        }

        // 2.0 Approve member seat request (TRANSACTION-SAFE)
        [HttpPost("{membershipId}/approve")]
        public async Task<IActionResult> ApproveMember(
            int institutionId,
            int membershipId,
            [FromBody] MemberApprovalRequest request)
        {
            var adminUserId = User.GetUserId();

            var membership = await _db.InstitutionMemberships
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == membershipId && m.InstitutionId == institutionId);

            if (membership == null)
                return NotFound("Membership not found.");

            if (membership.Status != MembershipStatus.PendingApproval)
                return BadRequest("Only pending members can be approved.");

            try
            {
                // ✅ Seat enforcement + membership update happen in ONE SERIALIZABLE TX (race-safe)
                await _seatService.ExecuteWithSeatEnforcementAsync(institutionId, membership.MemberType, async () =>
                {
                    membership.Status = MembershipStatus.Approved;
                    membership.IsActive = true; // ✅ Approved members consume seats
                    membership.ApprovedByUserId = adminUserId;
                    membership.ApprovedAt = DateTime.UtcNow;
                    membership.AdminNotes = request?.AdminNotes;
                    membership.UpdatedAt = DateTime.UtcNow;

                    // Activate user account
                    membership.User.IsApproved = true;

                    await _db.SaveChangesAsync();
                });
            }
            catch (InvalidOperationException ex)
            {
                // ✅ 409 Conflict with clear message
                return Conflict(new { message = ex.Message });
            }

            return Ok(new { message = "Member approved successfully." });
        }

        // 3.0 Reject member request (ensure it does NOT consume seats)
        [HttpPost("{membershipId}/reject")]
        public async Task<IActionResult> RejectMember(
            int institutionId,
            int membershipId,
            [FromBody] MemberApprovalRequest request)
        {
            var adminUserId = User.GetUserId();

            var membership = await _db.InstitutionMemberships
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == membershipId && m.InstitutionId == institutionId);

            if (membership == null)
                return NotFound("Membership not found.");

            if (membership.Status != MembershipStatus.PendingApproval)
                return BadRequest("Only pending members can be rejected.");

            membership.Status = MembershipStatus.Rejected;
            membership.IsActive = false; // ✅ does not consume seats
            membership.AdminNotes = request?.AdminNotes;
            membership.ApprovedByUserId = adminUserId;
            membership.ApprovedAt = DateTime.UtcNow;
            membership.UpdatedAt = DateTime.UtcNow;

            // Optional: keep user not approved
            membership.User.IsApproved = false;

            await _db.SaveChangesAsync();
            return Ok(new { message = "Member rejected." });
        }

        // 4.0 Deactivate members (frees seats)
        [HttpPost("{membershipId}/deactivate")]
        public async Task<IActionResult> DeactivateMember(int institutionId, int membershipId)
        {
            var membership = await _db.InstitutionMemberships
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == membershipId && m.InstitutionId == institutionId);

            if (membership == null)
                return NotFound("Membership not found.");

            if (!membership.IsActive)
                return BadRequest("Member already inactive.");

            membership.IsActive = false;
            membership.UpdatedAt = DateTime.UtcNow;

            // Optional: if deactivated, also mark user not approved for institution access
            membership.User.IsApproved = false;

            await _db.SaveChangesAsync();
            return Ok(new { message = "Member deactivated. Seat freed." });
        }

        // 5.0 Reactivate member (TRANSACTION-SAFE)
        [HttpPost("{membershipId}/reactivate")]
        public async Task<IActionResult> ReactivateMember(int institutionId, int membershipId)
        {
            var membership = await _db.InstitutionMemberships
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == membershipId && m.InstitutionId == institutionId);

            if (membership == null)
                return NotFound("Membership not found.");

            if (membership.IsActive)
                return BadRequest("Member already active.");

            if (membership.Status != MembershipStatus.Approved)
                return BadRequest("Only approved members can be reactivated.");

            try
            {
                // ✅ Seat enforcement + reactivation happen in ONE SERIALIZABLE TX (race-safe)
                await _seatService.ExecuteWithSeatEnforcementAsync(institutionId, membership.MemberType, async () =>
                {
                    membership.IsActive = true;
                    membership.UpdatedAt = DateTime.UtcNow;

                    membership.User.IsApproved = true;

                    await _db.SaveChangesAsync();
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }

            return Ok(new { message = "Member reactivated successfully." });
        }

        // 0.95 List approved members (still useful)
        [HttpGet("approved")]
        public async Task<IActionResult> GetApprovedMembers(int institutionId)
        {
            var approved = await _db.InstitutionMemberships
                .AsNoTracking()
                .Include(m => m.User)
                .Where(m =>
                    m.InstitutionId == institutionId &&
                    m.Status == MembershipStatus.Approved &&
                    m.IsActive == true)
                .OrderBy(m => m.User.FirstName)
                .Select(m => new
                {
                    membershipId = m.Id,
                    memberType = m.MemberType.ToString(),
                    status = m.Status.ToString(),
                    approvedAt = m.ApprovedAt,
                    user = new
                    {
                        id = m.User.Id,
                        username = m.User.Username,
                        email = m.User.Email,
                        firstName = m.User.FirstName,
                        lastName = m.User.LastName,
                        isApproved = m.User.IsApproved,
                        isActive = m.User.IsActive
                    }
                })
                .ToListAsync();

            return Ok(approved);
        }

        // ✅ 6.0 Change member type (Student ↔ Staff ↔ InstitutionAdmin) (TRANSACTION-SAFE when needed)
        [HttpPost("{membershipId}/change-type")]
        public async Task<IActionResult> ChangeMemberType(
            int institutionId,
            int membershipId,
            [FromBody] ChangeMemberTypeRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required.");

            var membership = await _db.InstitutionMemberships
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == membershipId && m.InstitutionId == institutionId);

            if (membership == null)
                return NotFound("Membership not found.");

            var fromType = membership.MemberType;
            var toType = request.MemberType;

            if (fromType == toType)
                return Ok(new { message = "No change.", fromType = fromType.ToString(), toType = toType.ToString() });

            // Only enforce if currently consuming a seat
            var isConsuming = membership.IsActive && membership.Status == MembershipStatus.Approved;

            bool fromIsStudent = fromType == InstitutionMemberType.Student;
            bool toIsStudent = toType == InstitutionMemberType.Student;

            bool fromIsStaffBucket = fromType == InstitutionMemberType.Staff || fromType == InstitutionMemberType.InstitutionAdmin;
            bool toIsStaffBucket = toType == InstitutionMemberType.Staff || toType == InstitutionMemberType.InstitutionAdmin;

            try
            {
                if (isConsuming)
                {
                    // Bucket changes that require capacity:
                    if (fromIsStudent && toIsStaffBucket)
                    {
                        // Need a STAFF seat (staff bucket includes InstitutionAdmin)
                        await _seatService.ExecuteWithSeatEnforcementAsync(institutionId, InstitutionMemberType.Staff, async () =>
                        {
                            membership.MemberType = toType;
                            membership.UpdatedAt = DateTime.UtcNow;
                            await _db.SaveChangesAsync();
                        });

                        return Ok(new
                        {
                            message = "Member type updated.",
                            fromType = fromType.ToString(),
                            toType = toType.ToString()
                        });
                    }

                    if (fromIsStaffBucket && toIsStudent)
                    {
                        // Need a STUDENT seat
                        await _seatService.ExecuteWithSeatEnforcementAsync(institutionId, InstitutionMemberType.Student, async () =>
                        {
                            membership.MemberType = toType;
                            membership.UpdatedAt = DateTime.UtcNow;
                            await _db.SaveChangesAsync();
                        });

                        return Ok(new
                        {
                            message = "Member type updated.",
                            fromType = fromType.ToString(),
                            toType = toType.ToString()
                        });
                    }

                    // Staff <-> Admin does not increase seats in any bucket; no enforcement needed
                }

                // Not consuming OR no seat-increasing bucket change => safe update
                membership.MemberType = toType;
                membership.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                return Ok(new
                {
                    message = "Member type updated.",
                    fromType = fromType.ToString(),
                    toType = toType.ToString()
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }
    }

    // ✅ Kept in same file to avoid extra file churn
    public class ChangeMemberTypeRequest
    {
        public InstitutionMemberType MemberType { get; set; }
    }
}
