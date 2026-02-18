using LawAfrica.API.DTOs.AI.Commentary;

namespace LawAfrica.API.Services.Ai.Commentary
{
    public interface ILegalCommentaryAiService
    {
        Task<LegalCommentaryAskResponseDto> AskAsync(int userId,
            LegalCommentaryAskRequestDto req,
            string userTier, // "basic" | "extended" (backend enforced)
            CancellationToken ct);
    }
}
