using System;
using System.Collections.Generic;

namespace LawAfrica.API.Models.DTOs.AdminDashboard
{
    public record PagedResult<T>(
        IReadOnlyList<T> Items,
        int Page,
        int PageSize,
        int TotalCount
    );

    public class AdminPagedQuery
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        // optional free-text search
        public string? Q { get; set; }

        // sorting key e.g. "createdAt_desc"
        public string? Sort { get; set; }

        // optional date range
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
    }

    // -------------------------
    // Institutions list
    // -------------------------
    public class AdminInstitutionsQuery : AdminPagedQuery
    {
        public bool? IsActive { get; set; }

        // computed filters
        public bool? IsLocked { get; set; }                 // no active sub now
        public bool? HasActiveSubscription { get; set; }    // active sub now
        public bool? AtCapacity { get; set; }               // seat used >= max (max > 0)
        public bool? SeatBlocked { get; set; }              // max == 0 for student or staff
    }

    public record AdminInstitutionRowDto(
        int InstitutionId,
        string Name,
        string EmailDomain,
        string OfficialEmail,
        bool IsActive,
        DateTime CreatedAtUtc,

        bool HasActiveSubscription,
        bool IsLocked,
        DateTime? NextSubscriptionEndUtc,

        int MaxStudentSeats,
        int MaxStaffSeats,
        int StudentUsed,
        int StaffUsed,
        int StudentPending,
        int StaffPending
    );

    // -------------------------
    // Subscriptions list
    // -------------------------
    public class AdminSubscriptionsQuery : AdminPagedQuery
    {
        public int? InstitutionId { get; set; }
        public int? ContentProductId { get; set; }

        // derived state by date: active/expired/upcoming (optional)
        public string? State { get; set; } // "active" | "expired" | "upcoming"

        // expiring soon window
        public int? ExpiringInDays { get; set; }
    }

    public record AdminSubscriptionRowDto(
        int SubscriptionId,
        int InstitutionId,
        string InstitutionName,
        int ContentProductId,
        string ContentProductName,
        DateTime StartUtc,
        DateTime EndUtc,
        bool IsActiveNow,
        string DerivedState
    );

    // -------------------------
    // Payments list
    // -------------------------
    public class AdminPaymentsQuery : AdminPagedQuery
    {
        public int? InstitutionId { get; set; }
        public int? UserId { get; set; }

        public string? PayerType { get; set; } // "institution" | "individual"

        public string? Status { get; set; }    // "Pending" | "PendingApproval" | "Success" | "Failed" | "Cancelled"
        public string? Purpose { get; set; }   // enum name e.g. "InstitutionProductSubscription"
        public string? Method { get; set; }    // enum name e.g. "Mpesa"
        public string? Provider { get; set; }  // enum name e.g. "Mpesa"

        public decimal? MinAmount { get; set; }
        public decimal? MaxAmount { get; set; }
        public string? Currency { get; set; }
    }

    public record AdminPaymentRowDto(
        int PaymentIntentId,
        DateTime CreatedAtUtc,

        string Status,
        string Purpose,
        string Method,
        string Provider,

        decimal Amount,
        string Currency,

        string PayerType,
        int? InstitutionId,
        string? InstitutionName,
        int? UserId,
        string? UserEmail,

        string? MpesaReceiptNumber,
        string? ManualReference
    );
}
