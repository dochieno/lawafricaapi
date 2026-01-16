namespace LawAfrica.API.Services.Payments
{
    public class PaystackOptions
    {
        public string SecretKey { get; set; } = "";
        public string? CallbackUrl { get; set; }

        // ✅ NEW: canonical public URL of the API (Render)
        // Example: https://lawafricaapi.onrender.com
        public string? PublicBaseUrl { get; set; }
    }
}
