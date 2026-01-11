namespace LawAfrica.API.Models.DTOs.AdminDashboard
{
    public class PaymentsKpiRequest
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public int? InstitutionId { get; set; } // optional filter
        public int? UserId { get; set; } // optional filter
    }
}
