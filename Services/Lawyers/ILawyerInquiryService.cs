using LawAfrica.API.DTOs.Lawyers;
using LawAfrica.API.Models.Lawyers;

namespace LawAfrica.API.Services.Lawyers
{
    public interface ILawyerInquiryService
    {
        Task<LawyerInquiry> CreateInquiryAsync(
            int requesterUserId,
            int? lawyerProfileId,
            int? practiceAreaId,
            int? townId,
            string problemSummary,
            string? preferredContactMethod,
            CancellationToken ct = default);

        // ✅ detail (requester OR assigned lawyer only)

        Task<List<LawyerInquiry>> GetMyInquiriesAsync(int requesterUserId, int take = 50, int skip = 0, CancellationToken ct = default);
        Task<List<LawyerInquiry>> GetLawyerInquiriesAsync(int lawyerUserId, int take = 50, int skip = 0, CancellationToken ct = default);
        Task<InquiryDetailDto?> GetInquiryDetailAsync(int inquiryId, int actorUserId, CancellationToken ct);

        // ✅ update status with permissions and transition rules
        Task<LawyerInquiry> UpdateStatusAsync(
            int inquiryId,
            int actorUserId,
            InquiryStatus status,
            InquiryOutcome? outcome,
            string? note,
            CancellationToken ct);

        // ✅ close helper
        Task<LawyerInquiry> CloseAsync(
            int inquiryId,
            int actorUserId,
            InquiryOutcome outcome,
            string? note,
            CancellationToken ct);

        // ✅ requester-only rating after close
        Task<LawyerInquiry> RateAsync(
            int inquiryId,
            int requesterUserId,
            int stars,
            string? comment,
            CancellationToken ct);
    }
}