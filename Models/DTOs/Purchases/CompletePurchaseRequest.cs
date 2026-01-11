namespace LawAfrica.API.Models.DTOs.Purchases
{
    /// <summary>
    /// Represents a confirmed public purchase.
    /// This is called AFTER payment success.
    /// </summary>
    public class CompletePurchaseRequest
    {
        public int ContentProductId { get; set; }

        /// <summary>
        /// External payment reference (Mpesa, Stripe, etc.)
        /// </summary>
        public string TransactionReference { get; set; } = string.Empty;
    }
}
