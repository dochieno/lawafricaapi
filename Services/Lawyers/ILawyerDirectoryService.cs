using LawAfrica.API.Models.Lawyers;

namespace LawAfrica.API.Services.Lawyers
{
    public interface ILawyerDirectoryService
    {
        Task<LawyerProfile?> GetLawyerAsync(int lawyerProfileId, CancellationToken ct = default);

        Task<List<LawyerProfile>> SearchLawyersAsync(
            int? countryId,
            int? townId,
            int? practiceAreaId,
            int? highestCourtAllowedId,
            bool verifiedOnly,
            string? q,
            int take = 30,
            int skip = 0,
            CancellationToken ct = default);
    }
}