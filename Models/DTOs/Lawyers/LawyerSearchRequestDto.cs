namespace LawAfrica.API.DTOs.Lawyers
{
    public class LawyerSearchRequestDto
    {
        public int? CountryId { get; set; }
        public int? TownId { get; set; }
        public int? PracticeAreaId { get; set; }
        public int? HighestCourtAllowedId { get; set; }
        public bool VerifiedOnly { get; set; } = true;
        public string? Q { get; set; }

        public int Take { get; set; } = 30;
        public int Skip { get; set; } = 0;
    }
}