namespace LawAfrica.API.Models.Emails
{
    public static class TemplateNames
    {
        public const string EmailVerification = "email-verification";
        public const string TwoFactorSetup = "twofactor-setup";

        // Optional next
        public const string PasswordReset = "password-reset";

        // Future-ready
        public const string InviteToInstitution = "invite-to-institution";
        public const string PaymentReceipt = "payment-receipt";
        public const string SubscriptionRenewal = "subscription-renewal";
    }
}
