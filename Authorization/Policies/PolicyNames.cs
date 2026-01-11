namespace LawAfrica.API.Authorization.Policies
{
    /// <summary>
    /// Central place for policy name constants.
    /// Prevents string duplication.
    /// </summary>
    public static class PolicyNames
    {
        public const string RequireApprovedUser = "RequireApprovedUser";
        public const string IsGlobalAdmin = "IsGlobalAdmin";
        public const string IsInstitutionAdmin = "IsInstitutionAdmin";
        public const string CanApproveInstitutionUsers = "CanApproveInstitutionUsers";
        public const string CanAccessLegalDocuments = "CanAccessLegalDocuments";
        public const string ApprovedUserOnly = "ApprovedUserOnly";

    }
}
