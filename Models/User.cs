using LawAfrica.API.Models;

public class User
{
    public int Id { get; set; }

    // ================================
    // Core Identity
    // ================================
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    // ================================
    // Domain Identity (NEW)
    // ================================
    /// <summary>
    /// Defines what category of user this is (Public, Institution, Student, Admin).
    /// Drives billing and access rules.
    /// </summary>
    public UserType UserType { get; set; } = UserType.Public;
    public int? InstitutionId { get; set; }
    public Institution? Institution { get; set; }
  
    public string Role { get; set; } = "User";

    public int? RoleId { get; set; }
    public Role? RoleEntity { get; set; }

    // ================================
    // Profile
    // ================================
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public int? CountryId { get; set; }
    public Country? Country { get; set; }
    public string? City { get; set; }
    public string? ProfileImageUrl { get; set; }

    // ================================
    // Account State
    // ================================
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Business-level approval gate.
    /// - Public users: true only after payment
    /// - Institution admins: true after onboarding
    /// - Students: true after domain validation
    /// </summary>
    public bool IsApproved { get; set; } = false;

    // ================================
    // Verification
    // ================================
    public bool IsEmailVerified { get; set; }
    public string? EmailVerificationToken { get; set; }
    public DateTime? EmailVerificationTokenExpiry { get; set; }
    public bool IsPhoneVerified { get; set; }

    // ================================
    // Two-Factor Authentication (MANDATORY)
    // ================================
    public string? TwoFactorSecret { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorCode { get; set; }
    public DateTime? TwoFactorExpiry { get; set; }

    // ================================
    // Password Reset
    // ================================
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiry { get; set; }
    // ================================
    // Account Lockout
    // ================================
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEndAt { get; set; }

    // ================================
    // Auditing
    // ================================
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    /// <summary>
    /// Superuser bypass. Global admins can do everything.
    /// </summary>
    public bool IsGlobalAdmin { get; set; } = false;


    // 2FA Setup Token (BEST PRACTICE)
    // ================================
    /// <summary>
    /// We store a HASH of the one-time setup token (not the token itself).
    /// The raw token is emailed to the user.
    /// </summary>
    public string? TwoFactorSetupTokenHash { get; set; }
    public DateTime? TwoFactorSetupTokenExpiry { get; set; }


}
