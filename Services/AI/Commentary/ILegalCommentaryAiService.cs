using LawAfrica.API.DTOs.AI.Commentary;

namespace LawAfrica.API.Services.Ai.Commentary
{
    public interface ILegalCommentaryAiService
    {
        Task<LegalCommentaryAskResponseDto> AskAsync(
            int userId,
            LegalCommentaryAskRequestDto req,
            string userTier,
            CancellationToken ct);

        // ✅ streaming
        IAsyncEnumerable<LegalCommentaryAiService.AskStreamChunk> AskStreamAsync(
            int userId,
            LegalCommentaryAskRequestDto req,
            string userTier,
            CancellationToken ct);
    }
}
