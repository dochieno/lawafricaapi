using LawAfrica.API.DTOs.AI;

namespace LawAfrica.API.Services.Ai
{
    public class LawReportChatService : ILawReportChatService
    {
        // ✅ Replace this with the REAL service you already use in your summarizer
        // e.g. OpenAiService, AiClient, AzureOpenAiClient, etc.
        private readonly IAiTextClient _ai;

        public LawReportChatService(IAiTextClient ai)
        {
            _ai = ai;
        }

        public async Task<LawReportChatResponseDto> AskAsync(
            int lawReportId,
            string caseTitle,
            string caseCitation,
            string caseContent,
            string userMessage,
            IReadOnlyList<LawReportChatTurnDto>? history,
            CancellationToken ct
        )
        {
            var content = caseContent ?? "";
            const int maxChars = 12000;
            if (content.Length > maxChars) content = content.Substring(0, maxChars);

            var system = $@"
                You are LegalAI for LawAfrica.

                Hard rules:
                - Use ONLY the CASE TEXT provided. If something is not clearly stated in the text, say: ""Not confirmed in the provided case text.""
                - Do NOT invent citations, paragraph numbers, page numbers, or quotations.
                - If the user asks for things not present (e.g., ''full list of authorities'' when none are visible), respond with what is available and state the limitation.

                Output format rules (IMPORTANT):
                - Prefer clean markdown.
                - Use UNORDERED BULLETS (-) for lists. Do NOT use numbered lists unless the user explicitly asks for numbering.
                - Use short section headings when helpful, like:
                  ### Issues
                  ### Holding
                  ### Reasoning
                  ### Orders
                  ### Authorities (if any)
                  ### Practical takeaways
                - Keep items concise (1–2 lines each). Avoid long paragraphs.

                Grounding:
                - When making an important claim, include a short supporting excerpt in quotes (max 18 words) from the CASE TEXT.
                  Example: - The court held X. ""<short excerpt>""
                - If you cannot find a supporting excerpt, say it is not confirmed.

                Case metadata:
                - Case title: {caseTitle}
                - Citation: {caseCitation}
                ";

            // Build a single prompt (works with “string in, string out” clients)
            var prompt = BuildPrompt(system, content, userMessage, history);

            // ✅ NO tuple deconstruction here
            var answer = await _ai.GenerateAsync(prompt, ct);

            return new LawReportChatResponseDto
            {
                LawReportId = lawReportId,
                Reply = answer ?? "",
                Model = _ai.ModelName ?? "" // optional
            };
        }

        private static string BuildPrompt(
            string system,
            string caseText,
            string userMessage,
            IReadOnlyList<LawReportChatTurnDto>? history)
        {
            var lines = new List<string>();
            lines.Add(system.Trim());

            // keep last 8 turns
            var turns = (history ?? Array.Empty<LawReportChatTurnDto>()).TakeLast(8);
            foreach (var t in turns)
            {
                var role = (t.Role ?? "user").Trim().ToLowerInvariant();
                if (role != "user" && role != "assistant") role = "user";
                var content = (t.Content ?? "").Trim();
                if (string.IsNullOrWhiteSpace(content)) continue;

                lines.Add($"{role.ToUpperInvariant()}: {content}");
            }

            lines.Add("CASE TEXT:");
            lines.Add(caseText);
            lines.Add("");
            lines.Add("QUESTION:");
            lines.Add(userMessage);

            return string.Join("\n", lines);
        }
    }
}