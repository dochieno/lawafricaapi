using Ganss.Xss;

namespace LawAfrica.API.Services.Html
{
    public static class ReportHtmlSanitizer
    {
        private static readonly string[] AllowedTags =
        {
            "p", "br",
            "h2", "h3", "h4",
            "strong", "b", "em", "i", "u",
            "blockquote",
            "ul", "ol", "li",
            "a",
            "table", "thead", "tbody", "tr", "th", "td",
            "hr"
        };

        private static readonly string[] AllowedAttributes =
        {
            "href", "title", "target", "rel",
            "colspan", "rowspan"
        };

        public static string Sanitize(string? html)
        {
            html ??= "";

            var sanitizer = new HtmlSanitizer();

            sanitizer.AllowedTags.Clear();
            foreach (var t in AllowedTags)
                sanitizer.AllowedTags.Add(t);

            sanitizer.AllowedAttributes.Clear();
            foreach (var a in AllowedAttributes)
                sanitizer.AllowedAttributes.Add(a);

            sanitizer.AllowedSchemes.Clear();
            sanitizer.AllowedSchemes.Add("http");
            sanitizer.AllowedSchemes.Add("https");
            sanitizer.AllowedSchemes.Add("mailto");

            // ❌ NO HtmlAgilityPack usage
            // ❌ No PostProcessNode

            return sanitizer.Sanitize(html);
        }
    }
}