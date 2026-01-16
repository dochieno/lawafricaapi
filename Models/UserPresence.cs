using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models
{
    public class UserPresence
    {
        [Key]
        public int UserId { get; set; }

        public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;

        public string? LastSeenIp { get; set; }
        public string? LastSeenUserAgent { get; set; }

        public User? User { get; set; }
    }
}
