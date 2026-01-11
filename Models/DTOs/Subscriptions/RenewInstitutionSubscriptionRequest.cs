using System;

namespace LawAfrica.API.Models.DTOs.Subscriptions
{
    public class RenewInstitutionSubscriptionRequest
    {
        public int DurationInMonths { get; set; }

        /// <summary>
        /// Optional. If provided, renewal starts on this date/time (UTC recommended).
        /// If omitted, Rule A applies:
        /// - if existing EndDate > now => renew from EndDate
        /// - else => renew from now
        /// </summary>
        public DateTime? StartDate { get; set; }
    }
}
