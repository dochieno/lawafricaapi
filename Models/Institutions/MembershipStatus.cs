namespace LawAfrica.API.Models.Institutions
{
    /// <summary>
    /// Controls onboarding + seat consumption.
    /// PendingApproval: created but NOT consuming seats
    /// Approved: eligible to consume seats when IsActive is true
    /// Rejected: denied
    /// Suspended: temporarily disabled (does not consume seats)
    /// </summary>
    public enum MembershipStatus
    {
        PendingApproval = 1,
        Approved = 2,
        Rejected = 3,
        Suspended = 4
    }
}
