using LawAfrica.API.Models;
using LawAfrica.API.Models.Institutions;

namespace LawAfrica.API.Models.DTOs.Registration
{
    /// <summary>
    /// DTO used to initiate a registration intent.
    /// This does NOT create a User.
    /// </summary>
    public class CreateRegistrationIntentRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }
        public int? CountryId { get; set; }

        public UserType UserType { get; set; }

        public int? InstitutionId { get; set; }

        // ================================
        // Institution Verification
        // ================================
        public string? InstitutionAccessCode { get; set; }
        public InstitutionMemberType? InstitutionMemberType { get; set; }

        public string? ReferenceNumber   { get; set; }
    }
}
