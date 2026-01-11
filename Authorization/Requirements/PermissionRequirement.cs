using Microsoft.AspNetCore.Authorization;

namespace LawAfrica.API.Authorization.Requirements
{
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string Code { get; }

        public PermissionRequirement(string code)
        {
            Code = code;
        }
    }
}
