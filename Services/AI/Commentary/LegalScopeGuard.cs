namespace LawAfrica.API.Services.Ai.Commentary
{
    /// <summary>
    /// Uses the existing IAiTextClient to classify whether a question is legal.
    /// Fails safe: if uncertain, decline.
    /// </summary>
    public class LegalScopeGuard : ILegalScopeGuard
    {
        private readonly IAiTextClient _ai;

        public LegalScopeGuard(IAiTextClient ai)
        {
            _ai = ai;
        }

        public async Task<(bool Ok, string? Reason)> IsLegalAsync(string question, CancellationToken ct)
        {
            var q = (question ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q))
                return (false, "Empty question.");

            // Strict classifier prompt:
            // - Returns ONLY LEGAL or NON_LEGAL
            // - We do not allow "maybe" responses; we fail safe.
            var prompt = $@"
You are a strict classifier for LawAfrica.

Return ONLY one of:
LEGAL
NON_LEGAL

Definition:
- LEGAL includes: laws, cases, legal procedure, rights, obligations, contracts, crime, civil claims, constitutional issues, regulations, compliance, courts, evidence, remedies, legal drafting.
- NON_LEGAL includes: medicine, coding, travel, relationships, general business advice not framed as law, entertainment, politics commentary not tied to a legal question.

Text:
{q}
".Trim();

            var raw = (await _ai.GenerateAsync(prompt, ct) ?? "")
                .Trim()
                .ToUpperInvariant();

            // Robust parse (model may include whitespace/newlines)
            if (raw.StartsWith("LEGAL")) return (true, null);
            if (raw.StartsWith("NON_LEGAL")) return (false, "Only legal questions are supported.");

            // Fail safe if ambiguous
            return (false, "Unable to confirm this is a legal question.");
        }
    }
}
