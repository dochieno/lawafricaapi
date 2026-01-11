using LawAfrica.API.Models;
using LawAfrica.API.Models.Institutions;

namespace LawAfrica.API.Models
{
    /// <summary>
    /// Temporary record used during signup BEFORE a real User is created.
    /// Contains all identity data required to create a User.
    /// </summary>
    public class RegistrationIntent
    {
        public int Id { get; set; }

        public string Email { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }   
        public int? CountryId { get; set; }
        public Country? Country { get; set; }   
        public UserType UserType { get; set; }
        public int? InstitutionId { get; set; }
        public Institution? Institution { get; set; }
        public bool PaymentCompleted { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        // ================================
        // Institution Verification Inputs
        // ================================

        /// <summary>
        /// Institution-provided access code (required for corporate institutions).
        /// </summary>
        public string? InstitutionAccessCode { get; set; }

        /// <summary>
        /// Reference number provided by the user (e.g. Student No, Employee No).
        /// </summary>
        public string? ReferenceNumber { get; set; }

        public bool IsConsumed { get; set; } = false;
        public DateTime? ConsumedAt { get; set; }
        public InstitutionMemberType? InstitutionMemberType { get; set; }


    }
}
