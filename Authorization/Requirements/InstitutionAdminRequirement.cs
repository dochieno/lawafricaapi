using Microsoft.AspNetCore.Authorization;

namespace LawAfrica.API.Authorization.Requirements
{
    /// <summary>
    /// Requires the user to be an Institution Admin.
    /// </summary>
    public class InstitutionAdminRequirement : IAuthorizationRequirement
    {
    }
}
