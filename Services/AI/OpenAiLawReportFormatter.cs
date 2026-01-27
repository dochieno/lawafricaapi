using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LawAfrica.API.Models.Ai;
using Microsoft.Extensions.Configuration;

namespace LawAfrica.API.Services.Ai
{
    public sealed class OpenAiLawReportFormatter : ILawReportFormatter
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public OpenAiLawReportFormatter(HttpClient http, IConfiguration config)
        {
            _http = http;
            _config = config;
        }

        public async Task<(AiLawReportFormatResult result, string modelUsed)> FormatRangesAsync(string rawText, CancellationToken ct)
        {
            var apiKey = GetString("OPENAI_API_KEY", "");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("OPENAI_API_KEY is not configured.");

            var model = GetString("AI_FORMATTER_MODEL", GetString("AI_MODEL", "gpt-4.1-mini"));
            var maxOutputTokens = GetInt("AI_FORMATTER_MAX_OUTPUT_TOKENS", 2500);

            var input = (rawText ?? "").Replace("\r\n", "\n").Trim();
            if (string.IsNullOrWhiteSpace(input))
                throw new InvalidOperationException("Law report has no ContentText.");

            var payload = new
            {
                model,
                max_output_tokens = maxOutputTokens,
                input = new object[]
                {
        new {
            role = "system",
            content = new object[]
            {
                new {
                    type = "input_text",   // ✅ FIX (was "text")
                    text =
                        @"You are a STRICT legal document formatter.
                        ABSOLUTE RULES:
                        - DO NOT rewrite, rephrase, correct, summarize, or add any words.
                        - DO NOT output any text from the document.
                        - You may ONLY output structure as character ranges into the provided input string.
                        - Ranges MUST refer to the exact input string (0-based char index, end exclusive).
                        - Blocks MUST be ordered, non-overlapping, and each block MUST have end > start.
                        - Prefer: title/meta in the first part; headings for section labels; paragraphs for prose; list_item for numbered/lettered items; divider/spacer only if clearly present.
                        Return ONLY JSON that matches the schema."
                }
            }
        },
        new {
            role = "user",
            content = new object[]
            {
                new {
                    type = "input_text",  // ✅ FIX (was "text")
                    text = input
                }
            }
        }
                },
                text = new
                {
                    format = new
                    {
                        type = "json_schema",
                        name = "law_report_block_ranges",
                        strict = true,
                        schema = BuildSchema()
                    }
                }
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"OpenAI formatter failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");

            var jsonText = ExtractOutputText(body);
            if (string.IsNullOrWhiteSpace(jsonText))
                throw new InvalidOperationException("OpenAI returned no structured JSON output.");

            var result = JsonSerializer.Deserialize<AiLawReportFormatResult>(jsonText, JsonOpts)
                         ?? throw new InvalidOperationException("Failed to parse formatter output JSON.");

            ValidateRanges(result, input.Length);

            return (result, model);
        }

        private static string ExtractOutputText(string responsesApiJson)
        {
            using var doc = JsonDocument.Parse(responsesApiJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
                return "";

            foreach (var outItem in output.EnumerateArray())
            {
                if (!outItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var c in content.EnumerateArray())
                {
                    // Prefer items that are output_text, but accept any with "text"
                    if (c.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                    {
                        var t = typeEl.GetString();
                        if (!string.Equals(t, "output_text", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(t, "text", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    if (c.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                        return textEl.GetString() ?? "";
                }
            }

            return "";
        }

        private static object BuildSchema()
        {
            return new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "blocks" },
                properties = new
                {
                    blocks = new
                    {
                        type = "array",
                        minItems = 1,
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,

                            // ✅ strict requires ALL keys in "properties" to be listed here
                            required = new[] { "type", "start", "end", "marker", "indent" },

                            properties = new
                            {
                                type = new
                                {
                                    type = "string",
                                    @enum = new[]
                                    {
                                "title", "meta", "heading", "paragraph", "list_item", "divider", "spacer"
                            }
                                },
                                start = new { type = "integer", minimum = 0 },
                                end = new { type = "integer", minimum = 1 },

                                // Optional by allowing null, but still required to appear
                                marker = new { type = new object[] { "string", "null" } },
                                indent = new { type = new object[] { "integer", "null" }, minimum = 0 }
                            }
                        }
                    }
                }
            };
        }

        private static void ValidateRanges(AiLawReportFormatResult result, int inputLen)
        {
            int prevEnd = 0;

            foreach (var b in result.Blocks ?? Enumerable.Empty<AiLawReportRangeBlock>())
            {
                if (b.Start < 0 || b.End < 0 || b.Start >= b.End)
                    throw new InvalidOperationException($"Invalid range: start={b.Start}, end={b.End}.");

                if (b.End > inputLen)
                    throw new InvalidOperationException($"Range exceeds input length: end={b.End}, len={inputLen}.");

                if (b.Start < prevEnd)
                    throw new InvalidOperationException($"Overlapping/out-of-order ranges detected. start={b.Start} < prevEnd={prevEnd}.");

                prevEnd = b.End;
            }
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