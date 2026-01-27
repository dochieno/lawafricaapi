using System.Threading;
using System.Threading.Tasks;

namespace LawAfrica.API.Services.LawReportsContent
{
    public interface ILawReportContentBuilder
    {
        /// <summary>
        /// Builds/refreshes row-per-block + JSON cache for a law report.
        /// If content hasn't changed and force=false, it can no-op.
        /// </summary>
        Task<BuildResult> BuildAsync(int lawReportId, bool force = false, CancellationToken ct = default);
    }

    public record BuildResult(
        int LawReportId,
        bool Built,
        string Hash,
        int BlocksWritten
    );
}