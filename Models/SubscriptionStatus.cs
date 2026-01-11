namespace LawAfrica.API.Models
{
    /// <summary>
    /// Represents the lifecycle state of a subscription-based product.
    /// Applies to both public and institution subscriptions.
    /// </summary>
    public enum SubscriptionStatus
    {
        /// <summary>
        /// Subscription exists but is not yet active
        /// (e.g. payment initiated but not confirmed).
        /// </summary>
        Pending = 1,

        /// <summary>
        /// Subscription is active and grants access.
        /// </summary>
        Active = 2,

        /// <summary>
        /// Subscription has naturally expired.
        /// </summary>
        Expired = 3,

        /// <summary>
        /// Subscription was manually suspended
        /// (e.g. non-payment, abuse, admin action).
        /// </summary>
        Suspended = 4
    }
}
