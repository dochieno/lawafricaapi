using LawAfrica.API.Models;

namespace LawAfrica.API.Models
{
    /// <summary>
    /// Represents an institution or firm (e.g. University, Law Firm, Bar Association).
    /// Institutions are onboarded by Global Admins and control access for their users.
    /// </summary>
    public class Institution
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? ShortName { get; set; }

        public string EmailDomain { get; set; } = string.Empty;

        public string OfficialEmail { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }

        public string? AlternatePhoneNumber { get; set; }

        public string? AddressLine1 { get; set; }

        public string? AddressLine2 { get; set; }

        public string? City { get; set; }

        public string? StateOrProvince { get; set; }

        public string? PostalCode { get; set; }

        public int? CountryId { get; set; }
        public Country? Country { get; set; }

        public string? RegistrationNumber { get; set; }

        public string? TaxPin { get; set; }

        public bool IsVerified { get; set; } = false;

        public bool IsActive { get; set; } = false;

        public DateTime? ActivatedAt { get; set; }

        public ICollection<User> Users { get; set; } = new List<User>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // ================================
        // Institution Type & Access Rules
        // ================================

        /// <summary>
        /// Determines how users under this institution are validated.
        /// </summary>
        public InstitutionType InstitutionType { get; set; } = InstitutionType.Academic;

        public string? InstitutionAccessCode { get; set; }

        public bool RequiresUserApproval { get; set; } = false;

        /// <summary>
        /// Seat limits.
        /// IMPORTANT RULE (your requested behavior):
        /// - 0 means NO seats allowed (hard block at access time)
        /// - N > 0 means allow up to N
        /// </summary>
        public int MaxStudentSeats { get; set; }

        /// <summary>
        /// Seat limits.
        /// IMPORTANT RULE (your requested behavior):
        /// - 0 means NO seats allowed (hard block at access time)
        /// - N > 0 means allow up to N
        /// </summary>
        public int MaxStaffSeats { get; set; }

        public bool AllowIndividualPurchasesWhenInstitutionInactive { get; set; } = false;

        // NOTE:
        // This nested enum is unusual because MembershipStatus is typically a separate enum
        // used by InstitutionMembership. Keeping it here unchanged to avoid breaking anything.
        public enum MembershipStatus
        {
            PendingApproval = 1,
            Approved = 2,
            Rejected = 3,
            Suspended = 4
        }
    }
}
