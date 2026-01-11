using System.Security.Claims;

namespace LawAfrica.API.Helpers
{
    public static class ClaimsPrincipalExtensions
    {
        public static int GetUserId(this ClaimsPrincipal user)
        {
            // Preferred (standard)
            var id =
                user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("userId"); // fallback

            if (string.IsNullOrEmpty(id))
                throw new UnauthorizedAccessException("User ID claim not found.");

            return int.Parse(id);
        }
    }
}
