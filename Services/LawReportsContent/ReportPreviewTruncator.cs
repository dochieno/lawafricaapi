using System.Text.RegularExpressions;

namespace LawAfrica.API.Services.LawReports
{
    public static class ReportPreviewTruncator
    {
        public static string ToPlainText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            // crude HTML strip (good enough for gating)
            var s = Regex.Replace(input, "<script[^>]*>.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<style[^>]*>.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<[^>]+>", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        public static string MakePreview(string rawContent, int maxChars, int maxParagraphs)
        {
            var text = ToPlainText(rawContent);

            // Split paragraphs by sentence-ish / line-ish groups
            var paras = Regex.Split(text, @"(?<=\.)\s+(?=[A-Z])")
                             .Where(x => !string.IsNullOrWhiteSpace(x))
                             .ToList();

            string preview;

            if (paras.Count > 0)
            {
                var take = Math.Max(1, Math.Min(maxParagraphs, paras.Count));
                preview = string.Join("\n\n", paras.Take(take));
            }
            else
            {
                preview = text;
            }

            if (preview.Length > maxChars)
                preview = preview.Substring(0, maxChars).TrimEnd();

            return preview;
        }
    }
}
