using LawAfrica.API.Authorization.Requirements;
using LawAfrica.API.Data;
using Microsoft.AspNetCore.Authorization;

namespace LawAfrica.API.Authorization.Handlers
{
    /// <summary>
    /// Verifies whether the current user is a true Global Admin (not just Role=Admin).
    /// Uses Users.IsGlobalAdmin (boolean).
    /// </summary>
    public class GlobalAdminHandler : AuthorizationHandler<GlobalAdminRequirement>
    {
        private readonly ApplicationDbContext _db;

        public GlobalAdminHandler(ApplicationDbContext db)
        {
            _db = db;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            GlobalAdminRequirement requirement)
        {
            var userIdClaim = context.User.FindFirst("userId");
            if (userIdClaim == null) return;

            if (!int.TryParse(userIdClaim.Value, out var userId)) return;

            var user = await _db.Users.FindAsync(userId);
            if (user == null) return;

            // ✅ HARD RULE: must be explicitly promoted
            if (user.IsGlobalAdmin)
            {
                context.Succeed(requirement);
            }
        }
    }
}
