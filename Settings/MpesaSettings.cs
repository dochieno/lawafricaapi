namespace LawAfrica.API.Settings
{
    public class MpesaSettings
    {
        public string Environment { get; set; } = "sandbox"; // sandbox | production

        public string ConsumerKey { get; set; } = string.Empty;
        public string ConsumerSecret { get; set; } = string.Empty;

        public string ShortCode { get; set; } = string.Empty;
        public string PassKey { get; set; } = string.Empty;

        public string InitiatorName { get; set; } = string.Empty; // optional depending on API used
        public string BaseUrl { get; set; } = string.Empty;       // e.g. https://sandbox.safaricom.co.ke

        public string CallbackUrl { get; set; } = string.Empty;   // your API callback URL
    }
}
