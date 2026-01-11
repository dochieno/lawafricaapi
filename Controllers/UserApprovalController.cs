using LawAfrica.API.Data;
using LawAfrica.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/users/approval")]
    [Authorize(Roles = "Admin,InstitutionAdmin")]
    public class UserApprovalController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserApprovalService _approvalService;

        public UserApprovalController(
            ApplicationDbContext db,
            UserApprovalService approvalService)
        {
            _db = db;
            _approvalService = approvalService;
        }

        /// <summary>
        /// Approves a user (Institution Admin or Global Admin).
        /// </summary>
        [HttpPost("{userId}/approve")]
        [Authorize(Roles = "Admin,InstitutionAdmin")]
        public async Task<IActionResult> Approve(int userId)
        {
            var approverId = int.Parse(User.FindFirst("userId")!.Value);

            await _approvalService.ApproveUserAsync(userId, approverId);

            return Ok(new { message = "User approved successfully." });
        }

        /// <summary>
        /// Rejects a user.
        /// </summary>
        [HttpPost("{userId}/reject")]
        [Authorize(Roles = "Admin,InstitutionAdmin")]
        public async Task<IActionResult> Reject(int userId, [FromBody] string reason)
        {
            var approverId = int.Parse(User.FindFirst("userId")!.Value);

            await _approvalService.RejectUserAsync(userId, approverId, reason);

            return Ok(new { message = "User rejected." });
        }
    }
}
