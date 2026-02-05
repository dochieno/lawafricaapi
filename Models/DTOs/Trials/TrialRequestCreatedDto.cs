namespace LawAfrica.API.Models.DTOs.Trials
{
    public class TrialRequestCreatedDto
    {
        public int RequestId { get; set; }
        public string Status { get; set; } = "Pending";

        // ✅ UX: help UI show what user requested
        public int ContentProductId { get; set; }
        public string? ContentProductName { get; set; }
    }
}
