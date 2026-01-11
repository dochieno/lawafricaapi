namespace LawAfrica.API.Models.Emails
{
    public class RenderedEmail
    {
        public string Subject { get; set; } = string.Empty;
        public string Html { get; set; } = string.Empty;
        public List<EmailInlineImage> InlineImages { get; set; } = new();
    }
}
