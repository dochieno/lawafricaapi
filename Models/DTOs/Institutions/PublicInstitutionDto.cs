namespace LawAfrica.API.Models.DTOs.Institutions
{
    public class PublicInstitutionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string EmailDomain { get; set; } = string.Empty;
        public bool RequiresAccessCode { get; set; }
    }
}
