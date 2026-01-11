using System;

namespace LawAfrica.API.Models.DTOs.Subscriptions
{
    public class SubscriptionActionRequestListDto
    {
        public int Id { get; set; }

        public int SubscriptionId { get; set; }

        public int InstitutionId { get; set; }
        public string InstitutionName { get; set; } = string.Empty;

        public int ContentProductId { get; set; }
        public string ContentProductName { get; set; } = string.Empty;

        public SubscriptionActionRequestType RequestType { get; set; }
        public SubscriptionActionRequestStatus Status { get; set; }

        public int RequestedByUserId { get; set; }
        public string RequestedByUsername { get; set; } = string.Empty;
        public string? RequestNotes { get; set; }

        public int? ReviewedByUserId { get; set; }
        public string? ReviewedByUsername { get; set; }
        public string? ReviewNotes { get; set; }
        public DateTime? ReviewedAt { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
