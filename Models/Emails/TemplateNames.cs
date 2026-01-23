// Models/Emails/TemplateNames.cs
namespace LawAfrica.API.Models.Emails
{
    public static class TemplateNames
    {
        public const string EmailVerification = "email-verification";
        public const string TwoFactorSetup = "twofactor-setup";

        public const string PasswordReset = "password-reset";
        public const string RegistrationResumeOtp = "registration-resume-otp";

        public const string InviteToInstitution = "invite-to-institution";
        public const string PaymentReceipt = "payment-receipt";
        public const string SubscriptionRenewal = "subscription-renewal";
        public const string InstitutionWelcome = "institution-welcome";

        // ✅ Invoice email template (PDF attachment)
        public const string InvoiceEmail = "invoice-email";
    }
}
