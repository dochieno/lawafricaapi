namespace LawAfrica.API.Models.DTOs.Payments
{
    /// <summary>
    /// Root callback payload from M-Pesa STK push.
    /// </summary>
    public class MpesaStkCallbackDto
    {
        public Body Body { get; set; } = null!;
    }

    public class Body
    {
        public StkCallback StkCallback { get; set; } = null!;
    }

    public class StkCallback
    {
        public string MerchantRequestID { get; set; } = string.Empty;
        public string CheckoutRequestID { get; set; } = string.Empty;
        public int ResultCode { get; set; }
        public string ResultDesc { get; set; } = string.Empty;
        public CallbackMetadata? CallbackMetadata { get; set; }
    }

    public class CallbackMetadata
    {
        public List<Item> Item { get; set; } = new();
    }

    public class Item
    {
        public string Name { get; set; } = string.Empty;
        public object? Value { get; set; }
    }
}
