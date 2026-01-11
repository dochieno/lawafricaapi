using System.Security.Claims;
using LawAfrica.API.Data;
using LawAfrica.API.Models.Institutions;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Institutions
{
    /// <summary>
    /// Authorization helper for institution-member admin actions.
    /// Does NOT change auth—only checks existing claims + DB membership.
    /// </summary>
    public class InstitutionMembershipAdminService
    {
        private readonly ApplicationDbContext _db;

        public InstitutionMembershipAdminService(ApplicationDbContext db)
        {
            _db = db;
        }

        public static int GetUserIdOrThrow(ClaimsPrincipal user)
        {
            // Common claim types depending on your JWT
            var idStr =
                user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                user.FindFirstValue("sub") ??
                user.FindFirstValue("userId");

            if (string.IsNullOrWhiteSpace(idStr) || !int.TryParse(idStr, out var userId))
                throw new UnauthorizedAccessException("User id not found in token.");

            return userId;
        }

        public static int? GetInstitutionIdClaim(ClaimsPrincipal user)
        {
            var raw =
                user.FindFirstValue("institutionId") ??
                user.FindFirstValue("InstitutionId");

            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (!int.TryParse(raw, out var n)) return null;
            return n;
        }

        public async Task EnsureIsInstitutionAdminAsync(int userId, int institutionId, CancellationToken ct = default)
        {
            var ok = await _db.InstitutionMemberships
                .AsNoTracking()
                .AnyAsync(m =>
                    m.UserId == userId &&
                    m.InstitutionId == institutionId &&
                    m.MemberType == InstitutionMemberType.InstitutionAdmin &&
                    m.Status == MembershipStatus.Approved &&
                    m.IsActive, ct);

            if (!ok)
                throw new UnauthorizedAccessException("You are not an active institution admin for this institution.");
        }
    }
}
