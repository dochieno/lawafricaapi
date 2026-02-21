namespace LawAfrica.API.DTOs.Lawyers
{
    public class LawyerListItemDto
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? FirmName { get; set; }
        public string? PrimaryTownName { get; set; }
        public int? PrimaryTownId { get; set; }
        public string? CountryName { get; set; }

        public bool IsVerified { get; set; }
        public string? HighestCourtName { get; set; }

        public string? ProfileImageUrl { get; set; } // from User.ProfileImageUrl
    }
}