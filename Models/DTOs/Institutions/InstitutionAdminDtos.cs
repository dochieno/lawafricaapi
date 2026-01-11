using System;
using LawAfrica.API.Models.Institutions;

namespace LawAfrica.API.Models.DTOs.Institutions
{
    public class InstitutionAdminListItemDto
    {
        public int Id { get; set; }                    // membershipId
        public int InstitutionId { get; set; }
        public string InstitutionName { get; set; } = string.Empty;

        public int UserId { get; set; }
        public string UserEmail { get; set; } = string.Empty;

        // Frontend expects a string
        public string Role { get; set; } = "InstitutionAdmin";

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpsertInstitutionAdminRequest
    {
        public int InstitutionId { get; set; }
        public string UserEmail { get; set; } = string.Empty;

        // Frontend sends this, but we map to MemberType = InstitutionAdmin
        public string Role { get; set; } = "InstitutionAdmin";

        public bool IsActive { get; set; } = true;
    }

    public class InstitutionMemberPendingDto
    {
        public int Id { get; set; }             // membershipId
        public int InstitutionId { get; set; }
        public int UserId { get; set; }

        public string? Username { get; set; }
        public string? Email { get; set; }

        public InstitutionMemberType MemberType { get; set; }
        public string ReferenceNumber { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }

    public class MembershipDecisionRequest
    {
        public string? AdminNotes { get; set; }
    }
}
