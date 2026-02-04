namespace LawAfrica.API.Models.Documents
{
    public sealed class DocumentEntitlementDecision
    {
        // Existing
        public DocumentAccessLevel AccessLevel { get; init; } = DocumentAccessLevel.PreviewOnly;
        public bool IsAllowed => AccessLevel == DocumentAccessLevel.FullAccess;
        public DocumentEntitlementDenyReason DenyReason { get; init; } = DocumentEntitlementDenyReason.None;
        public string? Message { get; init; }

        public bool CanPurchaseIndividually { get; set; } = true;
        public string? PurchaseDisabledReason { get; set; }

        // ✅ NEW: “Hard stop / subscribe to continue” popup support
        // Which product is required to unlock this document (e.g. LawAfrica Reports)
        public int? RequiredProductId { get; init; }
        public string? RequiredProductName { get; init; }

        // What action should UI show?
        // - "Subscribe" for reports bundle
        // - "Buy" for one-time documents (books, etc.)
        public EntitlementRequiredAction RequiredAction { get; init; } = EntitlementRequiredAction.None;

        // Premium gate UI labels (optional)
        public string? CtaLabel { get; init; }                 // e.g. "Subscribe to continue"
        public string? CtaUrl { get; init; }                   // e.g. "/pricing?product=reports"
        public string? SecondaryCtaLabel { get; init; }        // e.g. "View plans"
        public string? SecondaryCtaUrl { get; init; }

        // ✅ Preview policy (UI uses this to cut transcript & show “Read more”)
        // Keep this lightweight: do NOT embed preview text here; the UI already has contentText.
        public int? PreviewMaxChars { get; init; }             // e.g. 2200
        public int? PreviewMaxParagraphs { get; init; }        // e.g. 6
        public bool HardStop { get; init; } = false;           // If true: show gate even if doc is short

        // ✅ Debug/analytics (helps when support asks “why am I blocked?”)
        public EntitlementGrantSource GrantSource { get; init; } = EntitlementGrantSource.None;
        public string? DebugNote { get; init; }                // safe internal hint (optional)
    }

    public enum EntitlementRequiredAction
    {
        None = 0,
        Subscribe = 1,
        Buy = 2
    }

    public enum EntitlementGrantSource
    {
        None = 0,
        GlobalAdmin = 1,
        DirectPurchase = 2,
        LibraryGrant = 3,
        ProductOwnership = 4,
        PersonalSubscription = 5,
        InstitutionSubscription = 6
    }
}
