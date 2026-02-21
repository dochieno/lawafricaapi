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

        Task<List<LawyerInquiry>> GetMyInquiriesAsync(int requesterUserId, int take = 50, int skip = 0, CancellationToken ct = default);
        Task<List<LawyerInquiry>> GetLawyerInquiriesAsync(int lawyerUserId, int take = 50, int skip = 0, CancellationToken ct = default);
    }
}