namespace LawAfrica.API.Models
{
    /// <summary>
    /// Represents the domain-level identity of a user.
    /// This is NOT a role.
    /// Used to drive business rules such as billing,
    /// institution access, and approval workflows.
    /// </summary>
    public enum UserType
    {
        Public = 1,       // Individual users who pay before account creation
        Institution = 2,  // Institution administrators
        Student = 3,      // Institution-linked users
        Admin = 4         // Global platform administrators
    }
}

