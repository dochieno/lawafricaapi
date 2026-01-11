using LawAfrica.API.Models.Emails;

namespace LawAfrica.API.Services.Emails
{
    public interface IEmailTemplateRenderer
    {
        Task<RenderedEmail> RenderAsync(
            string templateName,
            string subject,
            object model,
            IEnumerable<EmailInlineImage>? inlineImages = null,
            CancellationToken ct = default);
    }
}
