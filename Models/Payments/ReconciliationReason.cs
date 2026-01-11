namespace LawAfrica.API.Models.Payments
{
    /// <summary>
    /// Detailed explanation of WHY a reconciliation item has its status.
    /// This answers: "What exactly is wrong (or right) with this payment?"
    /// </summary>
    public enum ReconciliationReason
    {
        /// <summary>
        /// No issue / clean match.
        /// </summary>
        None = 0,

        /// <summary>
        /// Amount paid at provider does not match internal intent amount.
        /// </summary>
        AmountMismatch = 1,

        /// <summary>
        /// Currency paid at provider does not match internal intent currency.
        /// </summary>
        CurrencyMismatch = 2,

        /// <summary>
        /// Provider transaction status conflicts with internal intent status.
        /// </summary>
        StatusMismatch = 3,

        /// <summary>
        /// Provider transaction exists but no internal PaymentIntent matches it.
        /// </summary>
        NoPaymentIntentForReference = 4,

        /// <summary>
        /// Internal PaymentIntent marked as successful but no provider transaction exists.
        /// </summary>
        NoProviderTransactionForIntent = 5,

        /// <summary>
        /// Multiple internal intents or provider transactions share the same reference.
        /// </summary>
        DuplicateReference = 6,

        /// <summary>
        /// Domain finalization failed (subscription not activated, entitlement missing, etc.).
        /// </summary>
        FinalizationError = 7,

        /// <summary>
        /// Admin manually resolved the reconciliation issue.
        /// </summary>
        ManualOverride = 8
    }
}
