namespace LawAfrica.API.Models.Payments
{
    /// <summary>
    /// How the payment was (or will be) made.
    /// </summary>
    public enum PaymentMethod
    {
        Mpesa = 1,
        BankTransfer = 2,
        ManualOverride = 3,

        // ✅ NEW
        Paystack = 4
    }
}
