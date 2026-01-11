namespace LawAfrica.API.Models.Payments
{
    public class PaymentHealingOptions
    {
        /// <summary>
        /// Only heal payments older than this, to avoid racing active webhooks.
        /// </summary>
        public int MinAgeMinutes { get; set; } = 2;

        /// <summary>
        /// Max number of intents handled per run (prevents long-running jobs).
        /// </summary>
        public int BatchSize { get; set; } = 200;
    }
}
