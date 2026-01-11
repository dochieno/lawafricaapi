namespace LawAfrica.API.Models
{
    public enum SubscriptionAuditAction
    {
        Unknown = 0,

        Created = 1,
        Extended = 2,
        Renewed = 3,
        Suspended = 4,
        Unsuspended = 5,

        // ✅ Phase 1: background job auto-status transitions
        AutoStatusChanged = 6
    }
}

