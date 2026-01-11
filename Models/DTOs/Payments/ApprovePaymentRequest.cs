namespace LawAfrica.API.Models.DTOs.Payments
{
    /// <summary>
    /// Approves a PendingApproval payment intent.
    /// </summary>
    public class ApprovePaymentRequest
    {
        public string? AdminNotes { get; set; }
    }
}
