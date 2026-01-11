namespace LawAfrica.API.Models.Documents
{
    public enum DocumentEntitlementDenyReason
    {
        None = 0,

        // NEW: used to drive the popup
        InstitutionSubscriptionInactive = 1001,

        // ✅ NEW: seat limit exceeded (block institution access)
        InstitutionSeatLimitExceeded = 1002,

        // Generic
        NotEntitled = 2000
    }
}

