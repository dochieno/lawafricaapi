using Microsoft.AspNetCore.Authorization;

namespace LawAfrica.API.Authorization.Requirements
{
    /// <summary>
    /// Determines whether a user can access a LegalDocument.
    /// Granularity (preview vs full) is handled elsewhere.
    /// </summary>
    public class CanAccessLegalDocumentRequirement : IAuthorizationRequirement
    {
    }
}
