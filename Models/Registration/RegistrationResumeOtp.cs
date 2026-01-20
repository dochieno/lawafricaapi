using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Registration
{
    public class RegistrationResumeOtp
    {
        public int Id { get; set; }

        [Required, MaxLength(256)]
        public string EmailNormalized { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string CodeHash { get; set; } = string.Empty;

        public DateTime ExpiresAtUtc { get; set; }

        public int Attempts { get; set; }

        public bool IsUsed { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime LastSentAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(64)]
        public string? IpAddress { get; set; }

        [MaxLength(400)]
        public string? UserAgent { get; set; }
    }
}
