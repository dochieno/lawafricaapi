namespace LawAfrica.API.Models.DTOs.Security
{
    public class ResendTwoFactorSetupRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
