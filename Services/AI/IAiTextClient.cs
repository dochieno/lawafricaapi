namespace LawAfrica.API.Services.Ai
{
    public interface IAiTextClient
    {
        string? ModelName { get; }
        Task<string> GenerateAsync(string prompt, CancellationToken ct);
    }
}