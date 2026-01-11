namespace LawAfrica.API.Models.DTOs.Subscriptions
{
    public class CreateInstitutionSubscriptionRequest
    {
        public int InstitutionId { get; set; }
        public int ContentProductId { get; set; }
        public int DurationInMonths { get; set; }

        /// <summary>
        /// Optional. If provided, subscription starts on this date/time (UTC recommended).
        /// If omitted, defaults to now (UTC).
        /// </summary>
        public DateTime? StartDate { get; set; }
    }
}
