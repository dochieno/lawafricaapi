using LawAfrica.API.Authorization.Requirements;
using LawAfrica.API.Data;
using LawAfrica.API.Models.Institutions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Authorization.Handlers
{
    /// <summary>
    /// Institution Admin authorization:
    /// - Global Admin bypass (IsGlobalAdmin = true)
    /// - Otherwise must be approved, active InstitutionMembership admin for the institutionId in route.
    /// </summary>
    public class InstitutionAdminHandler : AuthorizationHandler<InstitutionAdminRequirement>
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpContextAccessor _http;

        public InstitutionAdminHandler(ApplicationDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            InstitutionAdminRequirement requirement)
        {
            var userIdClaim = context.User.FindFirst("userId")?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return;

            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return;

            // ✅ Global Admin bypass
            if (user.IsGlobalAdmin)
            {
                context.Succeed(requirement);
                return;
            }

            // Route must contain institutionId
            var httpCtx = _http.HttpContext;
            if (httpCtx == null) return;

            if (!httpCtx.Request.RouteValues.TryGetValue("institutionId", out var routeVal))
                return;

            if (routeVal == null || !int.TryParse(routeVal.ToString(), out var institutionId))
                return;

            // ✅ Membership-based institution admin
            var isInstitutionAdmin = await _db.InstitutionMemberships
                .AsNoTracking()
                .AnyAsync(m =>
                    m.UserId == userId &&
                    m.InstitutionId == institutionId &&
                    m.MemberType == InstitutionMemberType.InstitutionAdmin &&
                    m.Status == MembershipStatus.Approved &&
                    m.IsActive);

            if (isInstitutionAdmin)
                context.Succeed(requirement);
        }
    }
}
