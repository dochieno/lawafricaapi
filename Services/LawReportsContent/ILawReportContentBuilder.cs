using System.Threading;
using System.Threading.Tasks;
using LawAfrica.API.Models.LawReportsContent;

namespace LawAfrica.API.Services.LawReportsContent
{
    public interface ILawReportContentBuilder
    {

        Task<BuildResult> BuildAsync(int lawReportId, bool force = false, CancellationToken ct = default);

        /// <summary>
        /// Builds/refreshes blocks using the AI formatter (ranges-only) and writes blocks + JSON cache.
        /// Returns the same BuildResult plus modelUsed for audit/debug.
        /// </summary>
        Task<(BuildResult result, string modelUsed)> BuildAiAsync(
            int lawReportId,
            bool force = false,
            int? maxInputCharsOverride = null,
            CancellationToken ct = default);

        Task<LawReportContentJsonDto> GetJsonDtoAsync(int lawReportId, CancellationToken ct = default);
    }

    public record BuildResult(
        int LawReportId,
        bool Built,
        string Hash,
        int BlocksWritten
    );
}