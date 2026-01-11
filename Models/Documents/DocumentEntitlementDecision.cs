namespace LawAfrica.API.Models.Documents
{
    public sealed class DocumentEntitlementDecision
    {
        public DocumentAccessLevel AccessLevel { get; init; } = DocumentAccessLevel.PreviewOnly;

        public bool IsAllowed => AccessLevel == DocumentAccessLevel.FullAccess;

        public DocumentEntitlementDenyReason DenyReason { get; init; } = DocumentEntitlementDenyReason.None;

        public string? Message { get; init; }

        public bool CanPurchaseIndividually { get; set; } = true;
        public string? PurchaseDisabledReason { get; set; }

    }
}
