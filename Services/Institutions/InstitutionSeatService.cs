using System.Data;
using LawAfrica.API.Data;
using LawAfrica.API.Models.Institutions;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Institutions
{
    /// <summary>
    /// Centralized seat usage calculations + enforcement.
    /// Seat consumption is based on (Approved + IsActive).
    ///
    /// IMPORTANT:
    /// - InstitutionAdmin counts toward STAFF seats (recommended).
    /// - For race-safety, use ExecuteWithSeatEnforcementAsync(...) to wrap check + write in one SERIALIZABLE transaction.
    /// </summary>
    public class InstitutionSeatService
    {
        private readonly ApplicationDbContext _db;

        public InstitutionSeatService(ApplicationDbContext db)
        {
            _db = db;
        }

        // ✅ Map member type into a seat bucket.
        // Admins consume STAFF seats.
        private static InstitutionMemberType SeatBucket(InstitutionMemberType memberType)
        {
            return memberType switch
            {
                InstitutionMemberType.Student => InstitutionMemberType.Student,
                InstitutionMemberType.Staff => InstitutionMemberType.Staff,
                InstitutionMemberType.InstitutionAdmin => InstitutionMemberType.Staff,
                _ => InstitutionMemberType.Staff
            };
        }

        public async Task<(int used, int? max)> GetSeatUsageAsync(int institutionId, InstitutionMemberType memberType)
        {
            var institution = await _db.Institutions.FindAsync(institutionId);
            if (institution == null)
                throw new InvalidOperationException("Institution not found.");

            // Use the bucket to determine max
            var bucket = SeatBucket(memberType);

            int? max = bucket switch
            {
                InstitutionMemberType.Student => institution.MaxStudentSeats,
                InstitutionMemberType.Staff => institution.MaxStaffSeats,
                _ => null
            };

            // Count usage by the EXACT memberType passed in:
            // - If caller asks Student => count Student rows
            // - If caller asks Staff => count Staff rows
            // - If caller asks InstitutionAdmin => count InstitutionAdmin rows
            //
            // Your controller currently does: staff + admin, so this supports that.
            var used = await _db.InstitutionMemberships.CountAsync(m =>
                m.InstitutionId == institutionId &&
                m.MemberType == memberType &&
                m.Status == MembershipStatus.Approved &&
                m.IsActive);

            return (used, max);
        }

        public async Task EnsureSeatAvailableAsync(int institutionId, InstitutionMemberType memberType)
        {
            // Admins count under STAFF bucket
            var bucket = SeatBucket(memberType);

            // If your system uses 0 or null as "unlimited", keep this behavior.
            // If you want 0 to mean "none allowed", adjust here.
            var institution = await _db.Institutions.FindAsync(institutionId);
            if (institution == null)
                throw new InvalidOperationException("Institution not found.");

            int? max = bucket switch
            {
                InstitutionMemberType.Student => institution.MaxStudentSeats,
                InstitutionMemberType.Staff => institution.MaxStaffSeats,
                _ => null
            };

            if (!max.HasValue || max.Value <= 0)
                return;

            // Count used seats in the bucket:
            // - Staff bucket includes Staff + InstitutionAdmin
            // - Student bucket includes Student
            int used = bucket == InstitutionMemberType.Student
                ? await _db.InstitutionMemberships.CountAsync(m =>
                    m.InstitutionId == institutionId &&
                    m.MemberType == InstitutionMemberType.Student &&
                    m.Status == MembershipStatus.Approved &&
                    m.IsActive)
                : await _db.InstitutionMemberships.CountAsync(m =>
                    m.InstitutionId == institutionId &&
                    (m.MemberType == InstitutionMemberType.Staff || m.MemberType == InstitutionMemberType.InstitutionAdmin) &&
                    m.Status == MembershipStatus.Approved &&
                    m.IsActive);

            if (used >= max.Value)
                throw new InvalidOperationException($"No available {bucket} seats. Limit reached.");
        }

        /// <summary>
        /// ✅ Transaction-safe seat enforcement (recommended for Step 3).
        /// Wraps:
        ///  - seat check
        ///  - your membership update (approve/reactivate/type-change)
        ///  - SaveChanges
        /// in ONE SERIALIZABLE transaction to avoid race conditions.
        ///
        /// Use this from controllers for:
        /// 1) Approve member
        /// 2) Reactivate member
        /// 3) Change member type Student ↔ Staff/Admin
        /// </summary>
        public async Task ExecuteWithSeatEnforcementAsync(
            int institutionId,
            InstitutionMemberType memberType,
            Func<Task> action,
            CancellationToken ct = default)
        {
            var hasOuterTx = _db.Database.CurrentTransaction != null;

            await using var tx = hasOuterTx
                ? null
                : await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            try
            {
                // Re-check seats INSIDE the serializable transaction
                await EnsureSeatAvailableAsync(institutionId, memberType);

                // Execute the write(s) inside the same transaction
                await action();

                if (tx != null)
                    await tx.CommitAsync(ct);
            }
            catch
            {
                if (tx != null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }
    }
}
