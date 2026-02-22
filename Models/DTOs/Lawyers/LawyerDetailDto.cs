using System.Collections.Generic;

namespace LawAfrica.API.DTOs.Lawyers
{
    public class LawyerServiceOfferingDto
    {
        public int LawyerServiceId { get; set; }
        public string ServiceName { get; set; } = "";
        public string? Currency { get; set; }
        public decimal? MinFee { get; set; }
        public decimal? MaxFee { get; set; }
        public string? BillingUnit { get; set; }
        public string? Notes { get; set; }
    }

    public class LawyerDetailDto
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? FirmName { get; set; }
        public string? Bio { get; set; }

        public bool IsVerified { get; set; }
        public string? HighestCourtName { get; set; }

        public string? PrimaryPhone { get; set; }
        public string? PublicEmail { get; set; }

        public int? PrimaryTownId { get; set; }
        public string? PrimaryTownName { get; set; }
        public string? CountryName { get; set; }

        public List<string> PracticeAreas { get; set; } = new();
        public List<string> TownsServed { get; set; } = new();

        // Google location (optional)
        public string? GoogleFormattedAddress { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public string? ProfileImageUrl { get; set; }

        // ✅ NEW: Services + rate card
        public List<LawyerServiceOfferingDto> ServiceOfferings { get; set; } = new();
    }
}