using System;
using System.Collections.Generic;

namespace LawAfrica.API.Models.DTOs.AdminDashboard
{
    public record DateValuePoint(DateTime DateUtc, decimal Value);

    public class AdminUsageSummaryQuery
    {
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }

        public int? InstitutionId { get; set; }
        public int? LegalDocumentId { get; set; }

        public string? Result { get; set; }     // "allowed" | "denied" (optional)
        public string? DenyReason { get; set; } // optional exact match
    }

    public record AdminUsageSummaryResponse(
        DateTime FromUtc,
        DateTime ToUtc,
        int Reads,
        int Blocks,
        decimal BlockRate,
        IReadOnlyList<DateValuePoint> ReadsByDay,
        IReadOnlyList<DateValuePoint> BlocksByDay,
        IReadOnlyList<KeyValuePoint> DeniesByReason,
        IReadOnlyList<KeyValuePoint> TopDocumentsByReads,
        IReadOnlyList<KeyValuePoint> TopInstitutionsByReads
    );

    public class AdminUsageEventsQuery : AdminPagedQuery
    {
        public int? InstitutionId { get; set; }
        public int? LegalDocumentId { get; set; }
        public int? UserId { get; set; }

        public string? Result { get; set; }     // "allowed" | "denied"
        public string? DenyReason { get; set; }
        public string? Surface { get; set; }
    }

    public record AdminUsageEventRowDto(
        long Id,
        DateTime AtUtc,
        int? UserId,
        int? InstitutionId,
        int LegalDocumentId,
        bool Allowed,
        string DecisionReason,
        string Surface,
        string IpAddress,
        string UserAgent
    );
}
