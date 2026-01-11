namespace LawAfrica.API.Models.DTOs
{
    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        // Optional
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public int? CountryId { get; set; }
        public string? CountryName { get; set; }

        public string? City { get; set; }    // Added property to match usage in AuthService
    }
}
