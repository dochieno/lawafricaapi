using Microsoft.AspNetCore.Authorization;

namespace LawAfrica.API.Authorization.Requirements
{
    /// <summary>
    /// Allows approval of institution users.
    /// </summary>
    public class CanApproveInstitutionUsersRequirement : IAuthorizationRequirement
    {
    }
}
