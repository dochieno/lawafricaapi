namespace LawAfrica.API.Models.DTOs.Registration
{
    public class VerifyResumeOtpRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty; // 6 digits
    }
}
