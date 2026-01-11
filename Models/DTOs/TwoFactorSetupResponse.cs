namespace LawAfrica.API.Models.DTOs
{
    public class TwoFactorSetupResponse
    {
        public string Secret { get; set; } = string.Empty;
        public string QrCodeUri { get; set; } = string.Empty;

        // For onboarding verification (no JWT)
        public string SetupToken { get; set; } = string.Empty;
        public DateTime SetupTokenExpiryUtc { get; set; }
    }
}

