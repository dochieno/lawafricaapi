namespace LawAfrica.API.Models.DTOs.Security
{
    public class VerifyTwoFactorSetupRequest
    {
        public string SetupToken { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }
}
