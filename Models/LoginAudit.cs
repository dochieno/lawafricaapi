namespace LawAfrica.API.Models
{
    public class LoginAudit
    {
        public int Id { get; set; }

        public int? UserId { get; set; } // nullable for unknown users
        public User User { get; set; } = null!;

        public string IpAddress { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public DateTime LoggedInAt { get; set; } = DateTime.UtcNow;
        public bool IsSuccessful { get; set; }
        public string FailureReason { get; set; } = string.Empty;
    }
}
