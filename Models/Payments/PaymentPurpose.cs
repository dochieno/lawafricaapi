namespace LawAfrica.API.Models.Payments
{
    public enum PaymentPurpose
    {
        PublicSignupFee = 1,
        PublicProductPurchase = 2,
        InstitutionProductSubscription = 3,

        // ✅ NEW:
        PublicLegalDocumentPurchase = 4
    }
}
