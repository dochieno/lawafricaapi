namespace LawAfrica.API.Services.Payments
{
    public class PaystackOptions
    {
        public string SecretKey { get; set; } = "";   // default avoids CS8618 warnings
        public string? CallbackUrl { get; set; }
    }
}
