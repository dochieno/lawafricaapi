using Microsoft.AspNetCore.Authorization;

namespace LawAfrica.API.Authorization.Requirements
{
    /// <summary>
    /// Ensures the user account is approved before accessing protected resources.
    /// </summary>
    public class ApprovedUserRequirement : IAuthorizationRequirement
    {
    }
}
