namespace LawAfrica.API.DTOs.Lawyers.Admin
{
    public class AdminLawyerServiceDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Slug { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
}