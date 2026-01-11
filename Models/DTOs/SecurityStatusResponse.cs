namespace LawAfrica.API.Models.DTOs.Security
{
    public class SecurityStatusResponse
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public bool IsEmailVerified { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }

        public string Role { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? City { get; set; }
    }
}
