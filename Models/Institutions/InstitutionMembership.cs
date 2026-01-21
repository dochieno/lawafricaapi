using LawAfrica.API.Models;

namespace LawAfrica.API.Models.Institutions
{
    /// <summary>
    /// Represents a user's membership inside an institution.
    /// This is the correct place to store member type, reference number,
    /// and approval state without bloating the User table.
    /// </summary>
    public class InstitutionMembership
    {
        public int Id { get; set; }

        // -------- Relations --------
        public int InstitutionId { get; set; }
        public Institution Institution { get; set; } = null!;

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        // -------- Classification --------
        public InstitutionMemberType MemberType { get; set; } = InstitutionMemberType.Student;

        /// <summary>
        /// Student reference number OR Employee number.
        /// Collected at RegistrationIntent stage to guide approval.
        /// </summary>
        public string? ReferenceNumber { get; set; } // nullable
        // -------- Governance --------
        public MembershipStatus Status { get; set; } = MembershipStatus.PendingApproval;

        /// <summary>
        /// If false, the member is disabled and does NOT consume seats.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public int? ApprovedByUserId { get; set; }
        public User? ApprovedByUser { get; set; }
        public DateTime? ApprovedAt { get; set; }

        public string? AdminNotes { get; set; }

        // -------- Audit --------
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
