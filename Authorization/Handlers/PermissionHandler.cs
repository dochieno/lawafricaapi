using LawAfrica.API.Authorization.Requirements;
using LawAfrica.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Authorization.Handlers
{
    /// <summary>
    /// DB-backed permission authorization.
    /// - Global Admin bypass
    /// - Otherwise must be Role=Admin and have the permission assigned in UserAdminPermissions.
    /// </summary>
    public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly ApplicationDbContext _db;

        public PermissionHandler(ApplicationDbContext db)
        {
            _db = db;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            var userIdClaim = context.User.FindFirst("userId")?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return;

            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return;

            // ✅ Global admin bypass
            if (user.IsGlobalAdmin)
            {
                context.Succeed(requirement);
                return;
            }

            // Must be staff admin
            if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                return;

            var hasPerm = await _db.UserAdminPermissions
                .AsNoTracking()
                .Include(x => x.Permission)
                .AnyAsync(x =>
                    x.UserId == userId &&
                    x.Permission.IsActive &&
                    x.Permission.Code == requirement.Code);

            if (hasPerm)
                context.Succeed(requirement);
        }
    }
}
