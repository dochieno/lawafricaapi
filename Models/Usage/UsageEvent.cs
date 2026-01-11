using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Usage
{
    public class UsageEvent
    {
        public long Id { get; set; }

        public DateTime AtUtc { get; set; } = DateTime.UtcNow;

        public int? UserId { get; set; }
        public int? InstitutionId { get; set; }

        // Your system reads legal documents; we track by LegalDocumentId
        public int LegalDocumentId { get; set; }

        public bool Allowed { get; set; }

        // "ALLOWED" or your deny reason key (e.g. SUBSCRIPTION_EXPIRED, SEAT_LIMIT_REACHED...)
        [MaxLength(120)]
        public string DecisionReason { get; set; } = "ALLOWED";

        // Optional: where did this happen? e.g. "ReaderOpen", "Download", "Api"
        [MaxLength(40)]
        public string Surface { get; set; } = "ReaderOpen";

        // Audit basics
        [MaxLength(64)]
        public string IpAddress { get; set; } = string.Empty;

        [MaxLength(400)]
        public string UserAgent { get; set; } = string.Empty;
    }
}
