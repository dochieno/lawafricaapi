using System.Threading;
using System.Threading.Tasks;
using LawAfrica.API.Models.DTOs.Ai.Sections;

namespace LawAfrica.API.Services.Ai.Sections
{
    public interface ILegalDocumentSectionSummarizer
    {
        Task<SectionSummaryResponseDto> SummarizeAsync(
            SectionSummaryRequestDto request,
            int userId,
            CancellationToken ct = default
        );
    }
}
