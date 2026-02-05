using LawAfrica.API.Models;

namespace LawAfrica.API.Models.DTOs.Trials
{
    public class TrialRequestListItemDto
    {
        public int Id { get; set; }
        public TrialRequestStatus Status { get; set; }
        public DateTime RequestedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? Reason { get; set; }
        public string? AdminNotes { get; set; }

        public TrialRequestUserDto User { get; set; } = new();
        public TrialRequestProductDto Product { get; set; } = new();
    }

    public class TrialRequestUserDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public UserType UserType { get; set; }
        public int? InstitutionId { get; set; }
    }

    public class TrialRequestProductDto
    {
        public int ContentProductId { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
