namespace LawAfrica.API.Services.Emails
{
    public interface IEmailTemplateStore
    {
        Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
        Task<string> GetTemplateAsync(string name, CancellationToken ct = default);
        Task<string> GetLayoutAsync(CancellationToken ct = default);
    }
}
