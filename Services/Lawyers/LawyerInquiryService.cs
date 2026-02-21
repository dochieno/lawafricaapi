using LawAfrica.API.Data;
using LawAfrica.API.Models.Lawyers;
using Microsoft.EntityFrameworkCore;
using System;

namespace LawAfrica.API.Services.Lawyers
{
    public class LawyerInquiryService : ILawyerInquiryService
    {
        private readonly ApplicationDbContext _db; // rename to your actual DbContext class

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

            // ensure requester exists (registered-only requirement)
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

            var inquiry = new LawyerInquiry
            {
                RequesterUserId = requesterUserId,
                LawyerProfileId = lawyerProfileId,
                PracticeAreaId = practiceAreaId,
                TownId = townId,
                ProblemSummary = problemSummary.Trim(),
                PreferredContactMethod = string.IsNullOrWhiteSpace(preferredContactMethod) ? null : preferredContactMethod.Trim(),
                Status = InquiryStatus.New,
                CreatedAt = DateTime.UtcNow
            };

            _db.LawyerInquiries.Add(inquiry);
            await _db.SaveChangesAsync(ct);

            return inquiry;
        }

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
            // find lawyer profile by userId
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
    }
}