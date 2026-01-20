using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Registration
{
    public class RegistrationResumeSession
    {
        public int Id { get; set; }

        [Required, MaxLength(128)]
        public string TokenHash { get; set; } = string.Empty;

        [Required, MaxLength(256)]
        public string EmailNormalized { get; set; } = string.Empty;

        public DateTime ExpiresAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? RevokedAtUtc { get; set; }
    }
}
