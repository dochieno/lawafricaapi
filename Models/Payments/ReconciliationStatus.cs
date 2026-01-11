namespace LawAfrica.API.Models.Payments
{
    /// <summary>
    /// High-level reconciliation outcome for a reconciliation item.
    /// This answers: "What is the state of this payment when compared
    /// to provider data and internal records?"
    /// </summary>
    public enum ReconciliationStatus
    {
        /// <summary>
        /// Provider transaction and internal intent match correctly
        /// (amount, currency, status, and finalization).
        /// </summary>
        Matched = 1,

        /// <summary>
        /// Something looks off and requires admin attention,
        /// but no hard failure yet (e.g. status mismatch).
        /// </summary>
        NeedsReview = 2,

        /// <summary>
        /// A hard mismatch exists (amount, currency, etc.).
        /// </summary>
        Mismatch = 3,

        /// <summary>
        /// Provider transaction exists but no internal PaymentIntent
        /// could be matched.
        /// </summary>
        MissingInternalIntent = 4,

        /// <summary>
        /// Internal PaymentIntent is marked successful but no provider
        /// transaction exists.
        /// </summary>
        MissingProviderTransaction = 5,

        /// <summary>
        /// Multiple internal intents or provider transactions match
        /// the same reference/transaction.
        /// </summary>
        Duplicate = 6,

        /// <summary>
        /// Payment is successful but domain finalization did not complete
        /// (subscription, entitlement, etc.).
        /// </summary>
        FinalizerFailed = 7,

        /// <summary>
        /// Issue was manually resolved by an admin through
        /// the manual reconciliation endpoint.
        /// </summary>
        ManuallyResolved = 8
    }
}
