using LawAfrica.API.Authorization.Requirements;
using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Authorization.Handlers
{
    public class CanAccessLegalDocumentHandler
        : AuthorizationHandler<CanAccessLegalDocumentRequirement, LegalDocument>
    {
        private readonly ApplicationDbContext _db;

        public CanAccessLegalDocumentHandler(ApplicationDbContext db)
        {
            _db = db;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            CanAccessLegalDocumentRequirement requirement,
            LegalDocument document)
        {
            // ---------------------------------
            // 1. Global admin always allowed
            // ---------------------------------
            if (context.User.IsInRole("GlobalAdmin"))
            {
                context.Succeed(requirement);
                return;
            }

            // ---------------------------------
            // 2. Free document
            // ---------------------------------
            if (!document.IsPremium)
            {
                context.Succeed(requirement);
                return;
            }

            // ---------------------------------
            // 3. User identity required
            // ---------------------------------
            int userId;
            try
            {
                userId = context.User.GetUserId();
            }
            catch
            {
                return; // no user → no access
            }

            // ---------------------------------
            // 4. Public ownership check
            // ---------------------------------
            var ownsProduct = await _db.UserProductOwnerships
                .AnyAsync(o =>
                    o.UserId == userId &&
                    o.ContentProductId == document.Id);

            if (ownsProduct)
            {
                context.Succeed(requirement);
                return;
            }

            // ---------------------------------
            // 5. Institution subscription check
            // ---------------------------------
            var institutionId = await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.InstitutionId)
                .FirstOrDefaultAsync();

            if (institutionId.HasValue)
            {
                var hasActiveSubscription = await _db.InstitutionProductSubscriptions
                    .AnyAsync(s =>
                        s.InstitutionId == institutionId.Value &&
                        s.ContentProductId == document.Id &&
                        s.Status == SubscriptionStatus.Active &&
                        s.EndDate >= DateTime.UtcNow);

                if (hasActiveSubscription)
                {
                    context.Succeed(requirement);
                    return;
                }
            }

            // ---------------------------------
            // No access
            // ---------------------------------
        }
    }
}
