namespace LawAfrica.API.Models.DTOs.Security
{
    public class AdminUserListItemDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Role { get; set; }
        public string UserType { get; set; } = "";
        public bool IsGlobalAdmin { get; set; }

        public bool IsActive { get; set; }
        public bool IsApproved { get; set; }
        public bool IsEmailVerified { get; set; }

        public int? InstitutionId { get; set; }
        public string? InstitutionName { get; set; }
        public string? Country { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public DateTime? LockoutEndAt { get; set; }
        public int FailedLoginAttempts { get; set; }

        public bool IsOnline { get; set; }
        public DateTime? LastSeenAtUtc { get; set; }
    }
}
