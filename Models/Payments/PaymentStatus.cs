namespace LawAfrica.API.Models.Payments
{
    public enum PaymentStatus
    {
        Pending = 1,          // Initiated, awaiting provider callback
        PendingApproval = 2,  // Manual/offline payment awaiting admin approval
        Success = 3,          // Confirmed paid
        Failed = 4,           // Provider or validation failure
        Cancelled = 5         // Optional (can be used for user cancellation / timeout)
    }
}
