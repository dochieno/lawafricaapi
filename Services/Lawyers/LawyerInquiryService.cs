using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Lawyers;
using LawAfrica.API.Models.Lawyers;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Lawyers
{
    public class LawyerInquiryService : ILawyerInquiryService
    {
        private readonly ApplicationDbContext _db;

        public LawyerInquiryService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<LawyerInquiry> CreateInquiryAsync(
            int requesterUserId,
            int? lawyerProfileId,
            int? practiceAreaId,
            int? townId,
            string problemSummary,
            string? preferredContactMethod,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(problemSummary))
                throw new ArgumentException("Problem summary is required.", nameof(problemSummary));

            var requesterExists = await _db.Users.AnyAsync(u => u.Id == requesterUserId && u.IsActive, ct);
            if (!requesterExists)
                throw new InvalidOperationException("Requester user does not exist or is inactive.");

            if (lawyerProfileId.HasValue)
            {
                var lawyerOk = await _db.LawyerProfiles.AnyAsync(lp =>
                    lp.Id == lawyerProfileId.Value &&
                    lp.IsActive &&
                    lp.VerificationStatus != LawyerVerificationStatus.Suspended, ct);

                if (!lawyerOk)
                    throw new InvalidOperationException("Selected lawyer is not available.");
            }

            var now = DateTime.UtcNow;

            var inquiry = new LawyerInquiry
            {
                RequesterUserId = requesterUserId,
                LawyerProfileId = lawyerProfileId,
                PracticeAreaId = practiceAreaId,
                TownId = townId,
                ProblemSummary = problemSummary.Trim(),
                PreferredContactMethod = string.IsNullOrWhiteSpace(preferredContactMethod) ? null : preferredContactMethod.Trim(),
                Status = InquiryStatus.New,
                CreatedAt = now,
                UpdatedAt = now,
                LastStatusChangedAtUtc = now
            };

            _db.LawyerInquiries.Add(inquiry);
            await _db.SaveChangesAsync(ct);

            return inquiry;
        }

        // Existing list methods you already use in controller (keep them)
        public async Task<List<LawyerInquiry>> GetMyInquiriesAsync(int requesterUserId, int take = 50, int skip = 0, CancellationToken ct = default)
        {
            return await _db.LawyerInquiries
                .AsNoTracking()
                .Where(x => x.RequesterUserId == requesterUserId)
                .OrderByDescending(x => x.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Include(x => x.LawyerProfile)
                .Include(x => x.PracticeArea)
                .Include(x => x.Town)
                .ToListAsync(ct);
        }

        public async Task<List<LawyerInquiry>> GetLawyerInquiriesAsync(int lawyerUserId, int take = 50, int skip = 0, CancellationToken ct = default)
        {
            var lawyerProfileId = await _db.LawyerProfiles
                .Where(lp => lp.UserId == lawyerUserId)
                .Select(lp => (int?)lp.Id)
                .FirstOrDefaultAsync(ct);

            if (!lawyerProfileId.HasValue)
                return new List<LawyerInquiry>();

            return await _db.LawyerInquiries
                .AsNoTracking()
                .Where(x => x.LawyerProfileId == lawyerProfileId.Value)
                .OrderByDescending(x => x.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Include(x => x.RequesterUser)
                .Include(x => x.PracticeArea)
                .Include(x => x.Town)
                .ToListAsync(ct);
        }

        // ==========================================================
        // ✅ NEW METHODS
        // ==========================================================

        public async Task<InquiryDetailDto?> GetInquiryDetailAsync(int inquiryId, int actorUserId, CancellationToken ct)
        {
            var inquiry = await _db.LawyerInquiries
                .AsNoTracking()
                .Include(x => x.PracticeArea)
                .Include(x => x.Town)
                .Include(x => x.RequesterUser)
                .Include(x => x.LawyerProfile)
                .FirstOrDefaultAsync(x => x.Id == inquiryId, ct);

            if (inquiry == null) return null;

            // Permission: requester OR assigned lawyer
            var isRequester = inquiry.RequesterUserId == actorUserId;
            var isAssignedLawyer = false;

            if (inquiry.LawyerProfileId.HasValue)
            {
                isAssignedLawyer = await _db.LawyerProfiles
                    .AsNoTracking()
                    .AnyAsync(lp => lp.Id == inquiry.LawyerProfileId.Value && lp.UserId == actorUserId, ct);
            }

            if (!isRequester && !isAssignedLawyer)
                throw new UnauthorizedAccessException("Not allowed.");

            var requesterName = inquiry.RequesterUser != null
                ? $"{(inquiry.RequesterUser.FirstName ?? "").Trim()} {(inquiry.RequesterUser.LastName ?? "").Trim()}".Trim()
                : null;

            return new InquiryDetailDto
            {
                Id = inquiry.Id,
                LawyerProfileId = inquiry.LawyerProfileId,
                RequesterUserId = inquiry.RequesterUserId,
                ProblemSummary = inquiry.ProblemSummary,
                Status = inquiry.Status.ToString(),
                CreatedAt = inquiry.CreatedAt,

                PracticeAreaName = inquiry.PracticeArea?.Name,
                TownName = inquiry.Town?.Name,

                RequesterName = requesterName,
                RequesterPhone = inquiry.RequesterUser?.PhoneNumber,
                RequesterEmail = inquiry.RequesterUser?.Email,

                PreferredContactMethod = inquiry.PreferredContactMethod,

                Outcome = inquiry.Outcome?.ToString(),
                LastStatusChangedAtUtc = inquiry.LastStatusChangedAtUtc,
                ContactedAtUtc = inquiry.ContactedAtUtc,
                InProgressAtUtc = inquiry.InProgressAtUtc,
                ClosedAtUtc = inquiry.ClosedAtUtc,

                ClosedByUserId = inquiry.ClosedByUserId,
                CloseNote = inquiry.CloseNote,

                RatingStars = inquiry.RatingStars,
                RatingComment = inquiry.RatingComment,
                RatedAtUtc = inquiry.RatedAtUtc,

                LawyerDisplayName = inquiry.LawyerProfile?.DisplayName,
                LawyerFirmName = inquiry.LawyerProfile?.FirmName,
                LawyerPrimaryPhone = inquiry.LawyerProfile?.PrimaryPhone,
                LawyerPublicEmail = inquiry.LawyerProfile?.PublicEmail
            };
        }

        public async Task<LawyerInquiry> UpdateStatusAsync(
            int inquiryId,
            int actorUserId,
            InquiryStatus status,
            InquiryOutcome? outcome,
            string? note,
            CancellationToken ct)
        {
            var inquiry = await _db.LawyerInquiries
                .Include(x => x.LawyerProfile)
                .FirstOrDefaultAsync(x => x.Id == inquiryId, ct);

            if (inquiry == null)
                throw new KeyNotFoundException("Inquiry not found.");

            var isRequester = inquiry.RequesterUserId == actorUserId;

            // Lawyer check (must be assigned lawyer)
            var isAssignedLawyer = false;
            if (inquiry.LawyerProfileId.HasValue)
            {
                isAssignedLawyer = await _db.LawyerProfiles
                    .AsNoTracking()
                    .AnyAsync(lp => lp.Id == inquiry.LawyerProfileId.Value && lp.UserId == actorUserId, ct);
            }

            // Permissions:
            // - requester can only close (Closed) (and optionally Spam if you want; we won’t)
            // - assigned lawyer can set Contacted/InProgress/Closed/Spam
            if (isRequester)
            {
                if (status != InquiryStatus.Closed)
                    throw new UnauthorizedAccessException("Requester can only close an inquiry.");
            }
            else if (!isAssignedLawyer)
            {
                throw new UnauthorizedAccessException("Not allowed.");
            }

            // Transition validation
            EnsureTransitionAllowed(inquiry.Status, status);

            var now = DateTime.UtcNow;

            inquiry.Status = status;
            inquiry.UpdatedAt = now;
            inquiry.LastStatusChangedAtUtc = now;

            if (status == InquiryStatus.Contacted)
                inquiry.ContactedAtUtc ??= now;

            if (status == InquiryStatus.InProgress)
                inquiry.InProgressAtUtc ??= now;

            if (status == InquiryStatus.Closed)
            {
                inquiry.ClosedAtUtc ??= now;
                inquiry.ClosedByUserId = actorUserId;

                if (outcome == null)
                    throw new InvalidOperationException("Outcome is required when closing an inquiry.");

                inquiry.Outcome = outcome;
                inquiry.CloseNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            }

            if (status == InquiryStatus.Spam)
            {
                // optional: clear close info; up to you
                inquiry.CloseNote = string.IsNullOrWhiteSpace(note) ? inquiry.CloseNote : note.Trim();
            }

            await _db.SaveChangesAsync(ct);
            return inquiry;
        }

        public Task<LawyerInquiry> CloseAsync(
            int inquiryId,
            int actorUserId,
            InquiryOutcome outcome,
            string? note,
            CancellationToken ct)
        {
            // convenience wrapper
            return UpdateStatusAsync(inquiryId, actorUserId, InquiryStatus.Closed, outcome, note, ct);
        }

        public async Task<LawyerInquiry> RateAsync(
            int inquiryId,
            int requesterUserId,
            int stars,
            string? comment,
            CancellationToken ct)
        {
            if (stars < 1 || stars > 5)
                throw new InvalidOperationException("Stars must be between 1 and 5.");

            var inquiry = await _db.LawyerInquiries
                .FirstOrDefaultAsync(x => x.Id == inquiryId, ct);

            if (inquiry == null)
                throw new KeyNotFoundException("Inquiry not found.");

            if (inquiry.RequesterUserId != requesterUserId)
                throw new UnauthorizedAccessException("Only the requester can rate this inquiry.");

            if (inquiry.Status != InquiryStatus.Closed)
                throw new InvalidOperationException("You can only rate after the inquiry is closed.");

            if (inquiry.RatingStars.HasValue)
                throw new InvalidOperationException("This inquiry has already been rated.");

            var now = DateTime.UtcNow;

            inquiry.RatingStars = stars;
            inquiry.RatingComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
            inquiry.RatedAtUtc = now;
            inquiry.UpdatedAt = now;

            await _db.SaveChangesAsync(ct);
            return inquiry;
        }

        private static void EnsureTransitionAllowed(InquiryStatus from, InquiryStatus to)
        {
            if (from == to) return;

            // terminal statuses
            if (from == InquiryStatus.Closed || from == InquiryStatus.Spam)
                throw new InvalidOperationException("This inquiry is already closed/spam and cannot be updated.");

            // allowed transitions
            var ok =
                (from == InquiryStatus.New && (to == InquiryStatus.Contacted || to == InquiryStatus.InProgress || to == InquiryStatus.Closed || to == InquiryStatus.Spam)) ||
                (from == InquiryStatus.Contacted && (to == InquiryStatus.InProgress || to == InquiryStatus.Closed || to == InquiryStatus.Spam)) ||
                (from == InquiryStatus.InProgress && (to == InquiryStatus.Closed || to == InquiryStatus.Spam));

            if (!ok)
                throw new InvalidOperationException($"Invalid status transition: {from} → {to}");
        }
    }
}