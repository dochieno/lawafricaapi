using LawAfrica.API.Authorization.Requirements;
using LawAfrica.API.Data;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace LawAfrica.API.Authorization.Handlers
{
    /// <summary>
    /// Authorization handler that checks User.IsApproved.
    /// </summary>
    public class ApprovedUserHandler : AuthorizationHandler<ApprovedUserRequirement>
    {
        private readonly ApplicationDbContext _db;

        public ApprovedUserHandler(ApplicationDbContext db)
        {
            _db = db;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ApprovedUserRequirement requirement)
        {
            var userIdClaim = context.User.FindFirst("userId");

            if (userIdClaim == null)
                return;

            if (!int.TryParse(userIdClaim.Value, out var userId))
                return;

            var user = await _db.Users.FindAsync(userId);

            if (user != null && user.IsApproved)
            {
                context.Succeed(requirement);
            }
        }
    }
}
