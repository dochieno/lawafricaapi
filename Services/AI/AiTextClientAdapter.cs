using OpenAI.Chat;

namespace LawAfrica.API.Services.Ai
{
    /// <summary>
    /// Thin adapter so other AI features (chat, formatting, etc.) can reuse
    /// the same OpenAI ChatClient + config pattern used by the summarizer.
    /// </summary>
    public class AiTextClientAdapter : IAiTextClient
    {
        private readonly ChatClient _chatClient;
        private readonly IConfiguration _config;

        public AiTextClientAdapter(ChatClient chatClient, IConfiguration config)
        {
            _chatClient = chatClient;
            _config = config;
        }

        public string? ModelName => GetString("AI_MODEL", "gpt-4o-mini");

        public async Task<string> GenerateAsync(string prompt, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return "";

            var maxOutputTokens = GetInt("AI_CHAT_MAX_OUTPUT_TOKENS", 600);

            var messages = new List<ChatMessage>
            {
                // We pass your built prompt as user content
                new UserChatMessage(prompt)
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = maxOutputTokens
            };

            ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, options, ct);

            var text = completion?.Content?.FirstOrDefault()?.Text?.Trim() ?? "";
            return text;
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