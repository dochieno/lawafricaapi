namespace LawAfrica.API.Models.DTOs.Registration
{
    public class PendingRegistrationResumeDto
    {
        public bool HasPending { get; set; }

        public int? RegistrationIntentId { get; set; }
        public string? Status { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? NextAction { get; set; }
    }
}
