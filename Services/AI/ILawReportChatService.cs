using LawAfrica.API.DTOs.AI;

namespace LawAfrica.API.Services.Ai
{
    public interface ILawReportChatService
    {
        Task<LawReportChatResponseDto> AskAsync(
            int lawReportId,
            string caseTitle,
            string caseCitation,
            string caseContent,
            string userMessage,
            IReadOnlyList<LawReportChatTurnDto>? history,
            CancellationToken ct
        );
    }
}