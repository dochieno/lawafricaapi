using Microsoft.AspNetCore.Authorization;

namespace LawAfrica.API.Authorization.Requirements
{
    /// <summary>
    /// Requires the user to be a Global Admin.
    /// </summary>
    public class GlobalAdminRequirement : IAuthorizationRequirement
    {
    }
}
