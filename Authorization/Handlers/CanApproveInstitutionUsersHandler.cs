using LawAfrica.API.Authorization.Requirements;
using LawAfrica.API.Data;
using LawAfrica.API.Models.Institutions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Authorization.Handlers
{
    /// <summary>
    /// Allows approving institution users if:
    /// - Global Admin (IsGlobalAdmin) OR
    /// - Staff Admin (Role=Admin) with permission users.approve OR
    /// - Institution Admin membership (approved+active) FOR the institutionId in route
    /// </summary>
    public class CanApproveInstitutionUsersHandler : AuthorizationHandler<CanApproveInstitutionUsersRequirement>
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpContextAccessor _http;

        private const string PERM = "users.approve";

        public CanApproveInstitutionUsersHandler(ApplicationDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            CanApproveInstitutionUsersRequirement requirement)
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

            // ✅ Staff Admin with explicit permission
            if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                var hasPerm = await _db.UserAdminPermissions
                    .AsNoTracking()
                    .Include(x => x.Permission)
                    .AnyAsync(x =>
                        x.UserId == userId &&
                        x.Permission.IsActive &&
                        x.Permission.Code == PERM);

                if (hasPerm)
                {
                    context.Succeed(requirement);
                    return;
                }
            }

            // ✅ Institution admin membership — MUST be scoped to institutionId route
            var httpCtx = _http.HttpContext;
            if (httpCtx == null) return;

            if (!httpCtx.Request.RouteValues.TryGetValue("institutionId", out var routeVal))
                return;

            if (routeVal == null || !int.TryParse(routeVal.ToString(), out var institutionId))
                return;

            var isInstAdmin = await _db.InstitutionMemberships
                .AsNoTracking()
                .AnyAsync(m =>
                    m.UserId == userId &&
                    m.InstitutionId == institutionId &&
                    m.MemberType == InstitutionMemberType.InstitutionAdmin &&
                    m.Status == MembershipStatus.Approved &&
                    m.IsActive);

            if (isInstAdmin)
                context.Succeed(requirement);
        }
    }
}
