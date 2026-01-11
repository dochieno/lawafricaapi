using LawAfrica.API.Models.Institutions;

namespace LawAfrica.API.Models.DTOs.Institutions
{
    public class InstitutionMemberDto
    {
        public int MembershipId { get; set; }
        public int UserId { get; set; }

        public string Email { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        public InstitutionMemberType MemberType { get; set; }
        public string ReferenceNumber { get; set; } = string.Empty;

        public MembershipStatus Status { get; set; }
        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }
}
