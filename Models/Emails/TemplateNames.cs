namespace LawAfrica.API.Models.Emails
{
    public static class TemplateNames
    {
        public const string EmailVerification = "email-verification";
        public const string TwoFactorSetup = "twofactor-setup";

        // Optional next
        public const string PasswordReset = "password-reset";
        public const string RegistrationResumeOtp = "registration-resume-otp";

        // Future-ready
        public const string InviteToInstitution = "invite-to-institution";
        public const string PaymentReceipt = "payment-receipt";
        public const string SubscriptionRenewal = "subscription-renewal";
        // ✅ NEW
        public const string InstitutionWelcome = "institution-welcome";
    }
}
