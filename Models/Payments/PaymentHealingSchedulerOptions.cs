namespace LawAfrica.API.Models.Payments
{
    public class PaymentHealingSchedulerOptions
    {
        /// <summary>
        /// Enable/disable the scheduler.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Run frequency in minutes.
        /// </summary>
        public int IntervalMinutes { get; set; } = 5;

        /// <summary>
        /// Optional delay after app start before first run.
        /// </summary>
        public int InitialDelaySeconds { get; set; } = 10;
    }
}
