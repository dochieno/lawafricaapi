using System;
using System.Collections.Generic;
using LawAfrica.API.Models;

namespace LawAfrica.API.Models.DTOs.Subscriptions
{
    public sealed class MySubscriptionsResponseDto
    {
        public int UserId { get; set; }
        public DateTime NowUtc { get; set; }
        public List<MySubscriptionItemDto> Items { get; set; } = new();
    }

    public sealed class MySubscriptionItemDto
    {
        public int Id { get; set; }
        public int ContentProductId { get; set; }
        public string ProductName { get; set; } = "";

        public SubscriptionStatus Status { get; set; }
        public bool IsTrial { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsActiveNow { get; set; }
        public int DaysRemaining { get; set; }
    }
}
