using LawAfrica.API.Models.Emails;
using System.Reflection;
using System.Text.Encodings.Web;

namespace LawAfrica.API.Services.Emails
{
    public class SimpleTokenEmailTemplateRenderer : IEmailTemplateRenderer
    {
        private readonly IEmailTemplateStore _store;

        public SimpleTokenEmailTemplateRenderer(IEmailTemplateStore store)
        {
            _store = store;
        }

        public async Task<RenderedEmail> RenderAsync(
            string templateName,
            string subject,
            object model,
            IEnumerable<EmailInlineImage>? inlineImages = null,
            CancellationToken ct = default)
        {
            var layout = await _store.GetLayoutAsync(ct);
            var template = await _store.GetTemplateAsync(templateName, ct);

            var body = ReplaceTokens(template, model);
            var html = layout.Replace("{{Body}}", body, StringComparison.Ordinal);
            html = ReplaceTokens(html, model);

            return new RenderedEmail
            {
                Subject = subject,
                Html = html,
                InlineImages = inlineImages?.ToList() ?? new List<EmailInlineImage>()
            };
        }

        private static string ReplaceTokens(string input, object model)
        {
            if (string.IsNullOrEmpty(input) || model == null)
                return input ?? string.Empty;

            var dict = ToFlatDictionary(model);

            foreach (var kv in dict)
            {
                var rawToken = "{{{" + kv.Key + "}}}";
                if (input.Contains(rawToken, StringComparison.Ordinal))
                    input = input.Replace(rawToken, kv.Value ?? "", StringComparison.Ordinal);
            }

            foreach (var kv in dict)
            {
                var token = "{{" + kv.Key + "}}";
                if (input.Contains(token, StringComparison.Ordinal))
                {
                    var safe = HtmlEncoder.Default.Encode(kv.Value ?? "");
                    input = input.Replace(token, safe, StringComparison.Ordinal);
                }
            }

            return input;
        }

        private static Dictionary<string, string?> ToFlatDictionary(object model)
        {
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                result[prop.Name] = prop.GetValue(model)?.ToString();
            }

            return result;
        }
    }
}
