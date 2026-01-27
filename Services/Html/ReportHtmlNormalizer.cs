using System.Text.RegularExpressions;

namespace LawAfrica.API.Services.Html
{
    public static class ReportHtmlNormalizer
    {
        // Word/Office tends to inject these
        private static readonly Regex RxMsoClasses = new(@"class\s*=\s*(""|')?Mso[^""'\s>]*\1?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxOfficeTags = new(@"</?o:p[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxXmlns = new(@"\s+xmlns(:\w+)?\s*=\s*(""[^""]*""|'[^']*')", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxStyleAttr = new(@"\s+style\s*=\s*(""[^""]*""|'[^']*')", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxLangDir = new(@"\s+(lang|dir)\s*=\s*(""[^""]*""|'[^']*')", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxMetaLinkScript = new(@"<(meta|link|script)[^>]*>.*?</\1>|<(meta|link|script)[^>]*/?>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        // tidy whitespace / Word artifacts
        private static readonly Regex RxNbspRuns = new(@"(&nbsp;|\u00A0){2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxEmptyParas = new(@"<p>\s*(<br\s*/?>)?\s*</p>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Normalize(string? html)
        {
            html ??= "";
            if (string.IsNullOrWhiteSpace(html)) return "";

            var x = html;

            // remove obvious dangerous/unwanted blocks (sanitizer also protects, but good to strip early)
            x = RxMetaLinkScript.Replace(x, "");

            // remove Office-specific tags/attrs
            x = RxOfficeTags.Replace(x, "");
            x = RxXmlns.Replace(x, "");
            x = RxMsoClasses.Replace(x, "");
            x = RxLangDir.Replace(x, "");

            // remove ALL inline styles (Lexis-like consistent styling)
            x = RxStyleAttr.Replace(x, "");

            // normalize nbsp spam
            x = RxNbspRuns.Replace(x, "&nbsp;");

            // remove tons of empty paragraphs
            x = RxEmptyParas.Replace(x, "<p></p>");

            // final trim
            x = x.Trim();

            return x;
        }
    }
}