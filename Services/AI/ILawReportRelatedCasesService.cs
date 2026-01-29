using OpenAI.Chat;
using System.Text.Json;
using LawAfrica.API.DTOs.Ai;
using LawAfrica.API.Models.Reports;

namespace LawAfrica.API.Services.Ai
{
    public interface ILawReportRelatedCasesService
    {
        Task<(List<AiRelatedCaseDto> items, string modelUsed)> FindRelatedCasesAsync(
            LawReport seed,
            int takeKenya,
            int takeForeign,
            CancellationToken ct
        );
    }

    public class OpenAiLawReportRelatedCasesService : ILawReportRelatedCasesService
    {
        private readonly ChatClient _chatClient;
        private readonly IConfiguration _config;

        public OpenAiLawReportRelatedCasesService(ChatClient chatClient, IConfiguration config)
        {
            _chatClient = chatClient;
            _config = config;
        }

        public async Task<(List<AiRelatedCaseDto> items, string modelUsed)> FindRelatedCasesAsync(
            LawReport seed,
            int takeKenya,
            int takeForeign,
            CancellationToken ct
        )
        {
            takeKenya = Math.Clamp(takeKenya, 1, 12);
            takeForeign = Math.Clamp(takeForeign, 0, 5);

            var maxInputChars = GetInt("AI_MAX_INPUT_CHARS", 12000);
            var maxOutputTokens = GetInt("AI_MAX_OUTPUT_TOKENS_RELATED", 900); // separate knob (optional)

            // Keep prompt small + safe
            var title = seed.LegalDocument?.Title ?? seed.Parties ?? $"Law Report {seed.Id}";
            var citation = (seed.Citation ?? "").Trim();
            var court = (seed.Court ?? "").Trim();
            var year = seed.Year;

            var content = (seed.ContentText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("Law report has no ContentText.");

            // For related cases, we don’t need the full text; shorten aggressively to reduce cost
            var excerptLimit = Math.Min(maxInputChars, 4500);
            if (content.Length > excerptLimit)
                content = content.Substring(0, excerptLimit);

            var system = BuildSystemPrompt(takeKenya, takeForeign);

            var user = $@"
            SEED CASE:
            Title: {title}
            Citation: {citation}
            Court: {court}
            Year: {year}

            TRANSCRIPT EXCERPT:
            {content}
            ".Trim();

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(system),
                new UserChatMessage(user)
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = maxOutputTokens
            };

            ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, options, ct);

            var raw = completion?.Content?.FirstOrDefault()?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("AI returned an empty related-cases response.");

            var list = TryParseJsonList(raw);

            // Defensive cleanup + enforce our Kenya/Foreign rule even if AI slips
            var kenya = new List<AiRelatedCaseDto>();
            var foreign = new List<AiRelatedCaseDto>();

            foreach (var x in list)
            {
                x.Title = (x.Title ?? "").Trim();
                x.Citation = string.IsNullOrWhiteSpace(x.Citation) ? null : x.Citation.Trim();
                x.Court = string.IsNullOrWhiteSpace(x.Court) ? null : x.Court.Trim();
                x.Url = string.IsNullOrWhiteSpace(x.Url) ? null : x.Url.Trim();
                x.Jurisdiction = string.IsNullOrWhiteSpace(x.Jurisdiction) ? "Kenya" : x.Jurisdiction.Trim();

                // If it says Kenya -> Kenya bucket
                if (x.Jurisdiction.Equals("Kenya", StringComparison.OrdinalIgnoreCase))
                {
                    x.Jurisdiction = "Kenya";
                    x.Note = null; // no disclaimer inside Kenya
                    kenya.Add(x);
                }
                else
                {
                    // foreign bucket: apply disclaimer note always
                    x.Note = "Outside Kenya — persuasive only. Verify independently.";
                    foreign.Add(x);
                }
            }

            // Enforce counts
            var result = new List<AiRelatedCaseDto>();
            result.AddRange(kenya.Take(takeKenya));
            result.AddRange(foreign.Take(takeForeign));

            // Model is known from config/DI, return for audit
            var modelUsed = GetString("AI_MODEL", "gpt-4o-mini");
            return (result, modelUsed);
        }

        private string BuildSystemPrompt(int takeKenya, int takeForeign)
        {
            // Strict JSON only; no markdown; no extra keys.
            return $@"
            You are a legal research assistant.

            You MUST return ONLY valid JSON (no markdown, no backticks, no prose).
            Return a JSON array of objects with EXACTLY these keys:
            Title (string), Citation (string|null), Year (number|null), Court (string|null),
            Jurisdiction (string), Url (string|null), Confidence (number|null), Note (string|null).

            Rules:
            - First include up to {takeKenya} related cases with Jurisdiction = ""Kenya"".
            - Then include up to {takeForeign} related cases outside Kenya (Jurisdiction must not be ""Kenya"").
            - For outside Kenya items, set Note = ""Outside Kenya — persuasive only. Verify independently.""
            - If you are unsure about Citation/Url, use null (do not invent).
            - Confidence is optional (0.0 to 1.0), else null.
            IMPORTANT: Do NOT suggest the same case as the current law report (do not repeat its title/parties/citation).

            Focus on similarity by: issues, legal principles, court level, remedy, and procedural posture.
            ".Trim();
        }

        private List<AiRelatedCaseDto> TryParseJsonList(string raw)
        {
            // Sometimes the model returns leading/trailing text by mistake.
            // We try to salvage by extracting the first JSON array.
            var json = ExtractJsonArray(raw);

            try
            {
                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                return JsonSerializer.Deserialize<List<AiRelatedCaseDto>>(json, opts) ?? new List<AiRelatedCaseDto>();
            }
            catch
            {
                return new List<AiRelatedCaseDto>();
            }
        }

        private static string ExtractJsonArray(string s)
        {
            var text = (s ?? "").Trim();
            if (text.StartsWith("[")) return text;

            var start = text.IndexOf('[');
            if (start < 0) return "[]";

            var depth = 0;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '[') depth++;
                else if (text[i] == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(start, i - start + 1);
                    }
                }
            }

            return "[]";
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