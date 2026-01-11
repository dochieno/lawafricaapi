namespace LawAfrica.API.Models
{
    public class Country
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // Optional future expansion:
        public string? IsoCode { get; set; }  // "KE", "UG", "TZ"
        public string? PhoneCode { get; set; } // "+254"

        // Navigation
        public ICollection<User> Users { get; set; } = new List<User>();
    }
}
