using OpenAI.Chat;
using System.Text;
using LawAfrica.API.Models.Ai;

namespace LawAfrica.API.Services.Ai
{
    public interface ILawReportSummarizer
    {
        Task<(string summary, string modelUsed)> SummarizeAsync(string contentText, AiSummaryType type, CancellationToken ct);
    }

    public class OpenAiLawReportSummarizer : ILawReportSummarizer
    {
        private readonly ChatClient _chatClient;
        private readonly IConfiguration _config;

        public OpenAiLawReportSummarizer(ChatClient chatClient, IConfiguration config)
        {
            _chatClient = chatClient;
            _config = config;
        }

        public async Task<(string summary, string modelUsed)> SummarizeAsync(string contentText, AiSummaryType type, CancellationToken ct)
        {
            var maxInputChars = GetInt("AI_MAX_INPUT_CHARS", 12000);
            var maxOutputTokens = GetInt("AI_MAX_OUTPUT_TOKENS", 700);

            var trimmed = (contentText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                throw new InvalidOperationException("Law report has no ContentText.");

            if (trimmed.Length > maxInputChars)
                trimmed = trimmed.Substring(0, maxInputChars);

            var system = BuildSystemPrompt(type);

            // Messages list approach keeps it clear and controllable
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(system),
                new UserChatMessage(trimmed)
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = maxOutputTokens
            };

            ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, options, ct);

            var text = completion?.Content?.FirstOrDefault()?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("AI returned an empty summary.");

            // Model is already known from DI, but we return it for audit
            var modelUsed = GetString("AI_MODEL", "gpt-4o-mini");
            return (text, modelUsed);
        }

        private string BuildSystemPrompt(AiSummaryType type)
        {
            // Very strict: no hallucinations, only summarize given text
            if (type == AiSummaryType.Extended)
            {
                return
                @"You are a legal summarization assistant.
                You MUST summarize ONLY the text provided by the user (the law report). Do not add outside facts or citations.
                If something is not clearly stated, write: ""Not stated in the report.""

                Return the summary in this exact structure:

                TITLE: (infer a short title if possible, otherwise ""Law Report Summary"")
                FACTS: 3-6 bullet points
                ISSUES: 2-5 bullet points
                HOLDING/DECISION: 1-3 bullet points
                REASONING: 4-8 bullet points
                KEY TAKEAWAYS: 3-6 bullet points

                Be concise and clear.";
                            }

                            // Basic
                            return
                @"You are a legal summarization assistant.
                Summarize ONLY the text provided by the user. Do not add outside facts or citations.
                If something is unclear, say: ""Not stated in the report.""

                Output:
                - A 1–2 paragraph summary
                - Then 5 key bullet points.";
        }

        private int GetInt(string key, int fallback)
        {
            var raw = _config[key];
            return int.TryParse(raw, out var n) ? n : fallback;
        }

        private string GetString(string key, string fallback)
        {
            var v = _config[key];
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }
    }
}