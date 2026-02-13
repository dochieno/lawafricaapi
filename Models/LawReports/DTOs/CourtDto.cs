namespace LawAfrica.API.Models.LawReports.DTOs
{
    public class CourtDto
    {
        public int Id { get; set; }
        public int CountryId { get; set; }

        public string Code { get; set; } = "";
        public string Name { get; set; } = "";

        public string Category { get; set; } = "Civil";

        public string? Abbreviation { get; set; }
        public int? Level { get; set; }

        public bool IsActive { get; set; }
        public int DisplayOrder { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
