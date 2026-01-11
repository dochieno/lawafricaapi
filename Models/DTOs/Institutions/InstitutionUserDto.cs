namespace LawAfrica.API.Models.DTOs.Institutions
{
    public class InstitutionUserDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }
}
