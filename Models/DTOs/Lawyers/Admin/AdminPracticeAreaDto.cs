namespace LawAfrica.API.DTOs.Lawyers.Admin
{
    public class AdminPracticeAreaDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Slug { get; set; }
        public bool IsActive { get; set; }
    }
}