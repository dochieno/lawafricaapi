using System;
using System.Collections.Generic;

namespace LawAfrica.API.Models.DTOs.AdminDashboard
{
    // ----------------------------
    // Query
    // ----------------------------
    public class AdminDashboardOverviewQuery
    {
        /// <summary>
        /// Inclusive range start (UTC). If null, defaults to last 30 days.
        /// </summary>
        public DateTime? FromUtc { get; set; }

        /// <summary>
        /// Exclusive range end (UTC). If null, defaults to now.
        /// </summary>
        public DateTime? ToUtc { get; set; }

        /// <summary>
        /// Days window for "expiring soon". Default 14.
        /// </summary>
        public int ExpiringSoonDays { get; set; } = 14;
    }

    // ----------------------------
    // Shared
    // ----------------------------
    public record KeyValuePoint(string Key, decimal Value);

    // ----------------------------
    // Response
    // ----------------------------
    public record AdminDashboardOverviewResponse(
        DateTime FromUtc,
        DateTime ToUtc,
        AdminInstitutionsKpis Institutions,
        AdminSubscriptionsKpis Subscriptions,
        AdminSeatsKpis Seats,
        AdminPaymentsKpis Payments,
        AdminUsageKpis Usage,
        AdminDenyReasonBreakdown DenyReasons,
        AdminPaymentsBreakdown PaymentsBreakdown
    );

    public record AdminInstitutionsKpis(
        int Total,
        int Active,
        int LockedBySubscription,
        int NewInPeriod
    );

    public record AdminSubscriptionsKpis(
        int Total,
        int ActiveNow,
        int InactiveNow,
        int ExpiringSoon,
        int ExpiringSoonDays
    );

    public record AdminSeatsKpis(
        int InstitutionsAtCapacity,
        int InstitutionsSeatBlocked,
        int StudentUsed,
        int StudentPending,
        int StaffUsed,
        int StaffPending,
        int AdminsUsed,
        int MaxStudentSeatsTotal,
        int MaxStaffSeatsTotal
    );

    public record AdminPaymentsKpis(
        int TotalCount,
        decimal TotalAmount,
        decimal SuccessAmount,
        int FailedCount,
        decimal InstitutionAmount,
        decimal IndividualAmount
    );

    public record AdminUsageKpis(
        int Reads,
        int Blocks,
        decimal BlockRate,
        IReadOnlyList<KeyValuePoint> TopDocuments,
        IReadOnlyList<KeyValuePoint> TopInstitutions
    );

    public record AdminDenyReasonBreakdown(
        IReadOnlyList<KeyValuePoint> ByReason
    );

    public record AdminPaymentsBreakdown(
        IReadOnlyList<KeyValuePoint> AmountByPurpose,
        IReadOnlyList<KeyValuePoint> CountByStatus
    );
}
