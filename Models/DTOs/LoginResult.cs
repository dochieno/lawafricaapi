namespace LawAfrica.API.Models.DTOs
{

    public class LoginResult
    {
        public bool Success { get; set; }
        public bool Requires2FA { get; set; }

        public bool Requires2FASetup { get; set; }

        public string? Token { get; set; }
        public int? UserId { get; set; }
        public string? Message { get; set; }

        /// <summary>
        /// Only used after 2FA confirmation (confirm-2fa endpoint).
        /// </summary>
        public static LoginResult SuccessResult(string token) => new()
        {
            Success = true,
            Token = token
        };

        public static LoginResult TwoFactorRequired(int userId) => new()
        {
            Success = true,
            Requires2FA = true,
            UserId = userId
        };

        public static LoginResult TwoFactorSetupRequired(int userId) => new()
        {
            Success = true,
            Requires2FASetup = true,
            UserId = userId
        };

        public static LoginResult Failed(string message) => new()
        {
            Success = false,
            Message = message
        };
    }
}
