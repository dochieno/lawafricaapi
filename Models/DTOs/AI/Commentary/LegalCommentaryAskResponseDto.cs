namespace LawAfrica.API.DTOs.AI.Commentary
{
    public class LegalCommentaryAskResponseDto
    {
        /// <summary>
        /// The active thread id for this conversation.
        /// Client should store and send it back on the next message to continue the same thread.
        /// </summary>
        public long ThreadId { get; set; }

        /// <summary>
        /// Main answer as markdown (frontend renders beautifully).
        /// </summary>
        public string ReplyMarkdown { get; set; } = "";

        /// <summary>
        /// Mandatory disclaimer (always show, even when declined).
        /// </summary>
        public string DisclaimerMarkdown { get; set; } = "";

        /// <summary>
        /// "basic" | "extended" (final applied mode after tier enforcement).
        /// </summary>
        public string Mode { get; set; } = "basic";

        public string Model { get; set; } = "";

        /// <summary>
        /// Internal sources used for grounding (for UI source drawer/cards).
        /// </summary>
        public List<LegalCommentarySourceDto> Sources { get; set; } = new();

        /// <summary>
        /// If non-legal / refused.
        /// </summary>
        public bool Declined { get; set; }

        public string? DeclineReason { get; set; }
    }

    public class LegalCommentarySourceDto
    {
        /// <summary>
        /// "law_report" | "pdf_page"
        /// </summary>
        public string Type { get; set; } = "";

        public int? LawReportId { get; set; }

        public int? LegalDocumentId { get; set; }

        public int? PageNumber { get; set; }

        /// <summary>
        /// Display title (for UI). For law_report: Parties. For pdf_page: "LegalDocument #ID".
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// For law_report: citation. For pdf_page: "Page X".
        /// </summary>
        public string Citation { get; set; } = "";

        /// <summary>
        /// Short excerpt used to ground the response.
        /// </summary>
        public string Snippet { get; set; } = "";

        /// <summary>
        /// Retrieval score/rank.
        /// </summary>
        public double Score { get; set; }
    }
}
