namespace LawAfrica.API.Services.Subscriptions
{
    public enum SubscriptionAccessDenyReason
    {
        None = 0,
        NoInstitution = 1,
        InstitutionInactive = 2,
        NoProducts = 3,
        NoSubscriptionRow = 4,

        // lock reasons
        Suspended = 10,
        Expired = 11,
        NotStarted = 12,
        NotEntitled = 13
    }

    public sealed class SubscriptionAccessDecision
    {
        /// <summary>
        /// True if institution subscription grants access right now.
        /// </summary>
        public bool IsAllowed { get; private set; }

        /// <summary>
        /// True if there is an institution subscription relationship for the product(s),
        /// but it is inactive so institution-managed access should be blocked for covered documents
        /// (caller decides whether to apply hard-block, e.g. even library grants).
        /// </summary>
        public bool IsInstitutionLock { get; private set; }

        public SubscriptionAccessDenyReason Reason { get; private set; }
        public string? Message { get; private set; }

        private SubscriptionAccessDecision() { }

        public static SubscriptionAccessDecision Allow(string? message = null) =>
            new SubscriptionAccessDecision
            {
                IsAllowed = true,
                IsInstitutionLock = false,
                Reason = SubscriptionAccessDenyReason.None,
                Message = message
            };

        public static SubscriptionAccessDecision Deny(SubscriptionAccessDenyReason reason, string? message = null) =>
            new SubscriptionAccessDecision
            {
                IsAllowed = false,
                IsInstitutionLock = false,
                Reason = reason,
                Message = message
            };

        public static SubscriptionAccessDecision Lock(SubscriptionAccessDenyReason reason, string? message = null) =>
            new SubscriptionAccessDecision
            {
                IsAllowed = false,
                IsInstitutionLock = true,
                Reason = reason,
                Message = message
            };
    }
}
