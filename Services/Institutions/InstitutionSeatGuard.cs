using System.Data;
using LawAfrica.API.Data;
using LawAfrica.API.Models.Institutions;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Institutions
{
    public record SeatUsageDto(
        int InstitutionId,
        int UsedStudentSeats,
        int? MaxStudentSeats,
        int UsedStaffSeats,
        int? MaxStaffSeats
    );

    public class SeatLimitExceededException : Exception
    {
        public int InstitutionId { get; }
        public InstitutionMemberType RequestedType { get; }
        public SeatUsageDto Usage { get; }

        public SeatLimitExceededException(int institutionId, InstitutionMemberType requestedType, SeatUsageDto usage, string message)
            : base(message)
        {
            InstitutionId = institutionId;
            RequestedType = requestedType;
            Usage = usage;
        }
    }

    /// <summary>
    /// Central seat enforcement guard (recommended approach).
    /// Enforces MaxStudentSeats / MaxStaffSeats using SERIALIZABLE transactions to avoid race conditions.
    ///
    /// Seats are consumed only by memberships that are:
    /// - Approved AND IsActive == true
    /// </summary>
    public class InstitutionSeatGuard
    {
        private readonly ApplicationDbContext _db;

        public InstitutionSeatGuard(ApplicationDbContext db)
        {
            _db = db;
        }

        // NOTE:
        // These helpers are fine for in-memory calculations,
        // but MUST NOT be used inside EF LINQ queries (they are not translatable to SQL).
        private static bool CountsAsStudent(InstitutionMemberType t)
            => t == InstitutionMemberType.Student;

        private static bool CountsAsStaff(InstitutionMemberType t)
            => t == InstitutionMemberType.Staff || t == InstitutionMemberType.InstitutionAdmin;

        private async Task<SeatUsageDto> GetUsageLockedAsync(int institutionId, CancellationToken ct)
        {
            var inst = await _db.Institutions
                .AsTracking()
                .FirstOrDefaultAsync(i => i.Id == institutionId, ct);

            if (inst == null)
                throw new InvalidOperationException("Institution not found.");

            var consuming = _db.InstitutionMemberships
                .Where(m =>
                    m.InstitutionId == institutionId &&
                    m.IsActive &&
                    m.Status == MembershipStatus.Approved);

            // ✅ EF-translatable predicates (no custom methods)
            var usedStudents = await consuming.CountAsync(
                m => m.MemberType == InstitutionMemberType.Student, ct);

            var usedStaff = await consuming.CountAsync(
                m => m.MemberType == InstitutionMemberType.Staff
                  || m.MemberType == InstitutionMemberType.InstitutionAdmin, ct);

            return new SeatUsageDto(
                InstitutionId: institutionId,
                UsedStudentSeats: usedStudents,
                MaxStudentSeats: inst.MaxStudentSeats,
                UsedStaffSeats: usedStaff,
                MaxStaffSeats: inst.MaxStaffSeats
            );
        }

        private static void ThrowIfExceeded(SeatUsageDto usage, InstitutionMemberType requestedType, int deltaStudents, int deltaStaff)
        {
            // If you use 0 as "unlimited", keep these checks.
            // If you want 0 to mean "no seats allowed", remove the >0 conditions.
            bool studentLimited = usage.MaxStudentSeats > 0;
            bool staffLimited = usage.MaxStaffSeats > 0;

            if (deltaStudents > 0 && studentLimited && usage.UsedStudentSeats + deltaStudents > usage.MaxStudentSeats)
            {
                throw new SeatLimitExceededException(
                    usage.InstitutionId,
                    requestedType,
                    usage,
                    $"Student seat limit reached ({usage.UsedStudentSeats}/{usage.MaxStudentSeats}).");
            }

            if (deltaStaff > 0 && staffLimited && usage.UsedStaffSeats + deltaStaff > usage.MaxStaffSeats)
            {
                throw new SeatLimitExceededException(
                    usage.InstitutionId,
                    requestedType,
                    usage,
                    $"Staff seat limit reached ({usage.UsedStaffSeats}/{usage.MaxStaffSeats}).");
            }
        }

        /// <summary>
        /// Call before creating / activating an Approved+Active membership that will consume a seat now.
        /// </summary>
        public async Task<SeatUsageDto> EnsureCanConsumeSeatForNewMembershipAsync(
            int institutionId,
            InstitutionMemberType requestedType,
            CancellationToken ct = default)
        {
            var hasOuterTx = _db.Database.CurrentTransaction != null;

            await using var tx = hasOuterTx
                ? null
                : await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            try
            {
                var usage = await GetUsageLockedAsync(institutionId, ct);

                int deltaStudents = CountsAsStudent(requestedType) ? 1 : 0;
                int deltaStaff = CountsAsStaff(requestedType) ? 1 : 0;

                ThrowIfExceeded(usage, requestedType, deltaStudents, deltaStaff);

                if (tx != null)
                    await tx.CommitAsync(ct);

                return usage;
            }
            catch
            {
                if (tx != null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }

        /// <summary>
        /// Call before changing an existing member's type (Student ↔ Staff/Admin).
        /// Only blocks if the change increases required seats, and only if the member is currently consuming a seat.
        /// </summary>
        public async Task<SeatUsageDto> EnsureCanChangeMemberTypeAsync(
            int institutionId,
            InstitutionMemberType fromType,
            InstitutionMemberType toType,
            bool isCurrentlyConsumingSeat,
            CancellationToken ct = default)
        {
            if (!isCurrentlyConsumingSeat)
            {
                // If not consuming seats, no enforcement needed.
                // Return usage for UI if desired.
                return await GetUsageLockedAsync(institutionId, ct);
            }

            var hasOuterTx = _db.Database.CurrentTransaction != null;

            await using var tx = hasOuterTx
                ? null
                : await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            try
            {
                var usage = await GetUsageLockedAsync(institutionId, ct);

                int deltaStudents =
                    (CountsAsStudent(toType) ? 1 : 0) - (CountsAsStudent(fromType) ? 1 : 0);

                int deltaStaff =
                    (CountsAsStaff(toType) ? 1 : 0) - (CountsAsStaff(fromType) ? 1 : 0);

                // Only blocks if delta is positive and exceeds max.
                ThrowIfExceeded(usage, toType, deltaStudents, deltaStaff);

                if (tx != null)
                    await tx.CommitAsync(ct);

                return usage;
            }
            catch
            {
                if (tx != null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }

        /// <summary>
        /// Optional: dashboard usage (not transaction-locked).
        /// </summary>
        public async Task<SeatUsageDto> GetUsageAsync(int institutionId, CancellationToken ct = default)
        {
            var inst = await _db.Institutions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == institutionId, ct);
            if (inst == null) throw new InvalidOperationException("Institution not found.");

            var consuming = _db.InstitutionMemberships
                .AsNoTracking()
                .Where(m =>
                    m.InstitutionId == institutionId &&
                    m.IsActive &&
                    m.Status == MembershipStatus.Approved);

            // ✅ EF-translatable predicates (no custom methods)
            var usedStudents = await consuming.CountAsync(
                m => m.MemberType == InstitutionMemberType.Student, ct);

            var usedStaff = await consuming.CountAsync(
                m => m.MemberType == InstitutionMemberType.Staff
                  || m.MemberType == InstitutionMemberType.InstitutionAdmin, ct);

            return new SeatUsageDto(institutionId, usedStudents, inst.MaxStudentSeats, usedStaff, inst.MaxStaffSeats);
        }
    }
}
