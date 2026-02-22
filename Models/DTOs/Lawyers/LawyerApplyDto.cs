using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Lawyers
{
    public class LawyerApplyDto
    {
        [Required, MaxLength(140)]
        public string DisplayName { get; set; } = string.Empty;

        [MaxLength(160)]
        public string? FirmName { get; set; }

        [MaxLength(2000)]
        public string? Bio { get; set; }

        [MaxLength(120)]
        public string? PrimaryPhone { get; set; }

        [MaxLength(160)]
        public string? PublicEmail { get; set; }

        // ✅ Required anchor for country validation (town has CountryId)
        [Range(1, int.MaxValue)]
        public int PrimaryTownId { get; set; }

        public int? HighestCourtAllowedId { get; set; }

        // Optional Google location
        [MaxLength(120)]
        public string? GooglePlaceId { get; set; }

        [MaxLength(240)]
        public string? GoogleFormattedAddress { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        // Replace lists on save
        public List<int> TownIdsServed { get; set; } = new();
        public List<int> PracticeAreaIds { get; set; } = new();
        public List<LawyerServiceOfferingUpsertDto> Services { get; set; } = new();
    }
}