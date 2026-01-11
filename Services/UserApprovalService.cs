using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services
{
    /// <summary>
    /// Central authority for approving and rejecting users.
    /// No controller should bypass this service.
    /// </summary>
    public class UserApprovalService
    {
        private readonly ApplicationDbContext _db;

        public UserApprovalService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Approves a pending user.
        /// </summary>
        public async Task ApproveUserAsync(int userId, int approverUserId)
        {
            var user = await _db.Users
                .Include(u => u.Institution)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new InvalidOperationException("User not found.");

            if (user.IsApproved)
                return;

            user.IsApproved = true;
            user.UpdatedAt = DateTime.UtcNow;

            _db.AuditEvents.Add(new AuditEvent
            {
                Action = "USER_APPROVED",
                EntityType = "User",
                EntityId = user.Id,
                PerformedByUserId = approverUserId
            });

            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Rejects a pending user.
        /// </summary>
        public async Task RejectUserAsync(int userId, int approverUserId, string reason)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new InvalidOperationException("User not found.");

            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;

            _db.AuditEvents.Add(new AuditEvent
            {
                Action = $"USER_REJECTED: {reason}",
                EntityType = "User",
                EntityId = user.Id,
                PerformedByUserId = approverUserId
            });

            await _db.SaveChangesAsync();
        }
    }
}
