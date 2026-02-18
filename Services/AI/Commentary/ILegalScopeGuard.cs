namespace LawAfrica.API.Services.Ai.Commentary
{
    /// <summary>
    /// Ensures Legal Commentary AI ONLY answers legal questions.
    /// Any non-legal question must be declined.
    /// </summary>
    public interface ILegalScopeGuard
    {
        Task<(bool Ok, string? Reason)> IsLegalAsync(string question, CancellationToken ct);
    }
}
