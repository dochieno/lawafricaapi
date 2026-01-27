using LawAfrica.API.Models.Ai;

namespace LawAfrica.API.Services.Ai
{
    public interface ILawReportFormatter
    {
        Task<(AiLawReportFormatResult result, string modelUsed)> FormatRangesAsync(string rawText, CancellationToken ct);
    }
}