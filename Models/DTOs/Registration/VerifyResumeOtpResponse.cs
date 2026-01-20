namespace LawAfrica.API.Models.DTOs.Registration
{
    public class VerifyResumeOtpResponse
    {
        public string ResumeToken { get; set; } = string.Empty;
        public PendingRegistrationResumeDto Pending { get; set; } = new PendingRegistrationResumeDto();
    }
}
