namespace LawAfrica.API.Models
{
    public class UserPresence
    {
        public int UserId { get; set; }
        public DateTime LastSeenAtUtc { get; set; }

        public User? User { get; set; }
    }
}
