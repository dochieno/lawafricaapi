namespace LawAfrica.API.Models.DTOs
{
    public class ProfileUpdateRequest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public int? CountryId { get; set; }
        public string? City { get; set; }
        public string? ProfileImageUrl { get; set; }

        // NEW: allow updating email (optional)
        public string? Email { get; set; }
    }
}
