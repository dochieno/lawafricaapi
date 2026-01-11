namespace LawAfrica.API.Models.DTOs.Payments
{
    public class PaystackReturnVisitRequest
    {
        public string Reference { get; set; } = string.Empty;
        public string? CurrentUrl { get; set; }
        public string? UserAgent { get; set; }
    }
}
