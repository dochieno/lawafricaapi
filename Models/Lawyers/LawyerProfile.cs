using LawAfrica.API.Models.LawReports.Models;
using LawAfrica.API.Models.Locations;
using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Lawyers
{
    public class LawyerProfile
    {
        public int Id { get; set; }

        // 1:1 with your User (int PK)
        public int UserId { get; set; }
        public User User { get; set; } = null!;

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

        // ✅ Highest court allowed (FK -> your Court model)
        public int? HighestCourtAllowedId { get; set; }
        public Court? HighestCourtAllowed { get; set; }

        // Optional base town
        public int? PrimaryTownId { get; set; }
        public Town? PrimaryTown { get; set; }

        // ✅ Google location (optional)
        [MaxLength(120)]
        public string? GooglePlaceId { get; set; }

        [MaxLength(240)]
        public string? GoogleFormattedAddress { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public LawyerVerificationStatus VerificationStatus { get; set; } = LawyerVerificationStatus.Pending;
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // navigation
        public ICollection<LawyerPracticeArea> PracticeAreas { get; set; } = new List<LawyerPracticeArea>();
        public ICollection<LawyerTown> TownsServed { get; set; } = new List<LawyerTown>();
        public ICollection<LawyerInquiry> Inquiries { get; set; } = new List<LawyerInquiry>();
    }
}