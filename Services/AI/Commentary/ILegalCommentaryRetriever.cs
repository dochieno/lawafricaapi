using LawAfrica.API.DTOs.AI.Commentary;

namespace LawAfrica.API.Services.Ai.Commentary
{
    /// <summary>
    /// Retrieves relevant internal LawAfrica sources (LawReports + LegalDocumentPageTexts)
    /// for grounding Legal Commentary AI.
    /// </summary>
    public interface ILegalCommentaryRetriever
    {
        Task<LegalCommentaryRetrievalResult> SearchAsync(
            string question,
            int maxItems,
            CancellationToken ct);
    }

    public class LegalCommentaryRetrievalResult
    {
        /// <summary>
        /// UI-friendly list of sources used for grounding.
        /// </summary>
        public List<LegalCommentarySourceDto> Sources { get; set; } = new();

        /// <summary>
        /// The compact sources pack inserted into the AI prompt.
        /// Must include source keys that the model can cite:
        /// - LAW_REPORT:{id}
        /// - PDF_PAGE:DOC={legalDocumentId}:PAGE={pageNumber}
        /// </summary>
        public string GroundingText { get; set; } = "";
    }
}
