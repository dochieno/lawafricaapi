namespace LawAfrica.API.Services.Ai.Sections
{
    public sealed class SectionTextExtractionResult
    {
        public string Text { get; init; } = string.Empty;
        public int CharCount { get; init; }

        public int PagesRequested { get; init; }
        public int PagesFound { get; init; }
        public List<int> MissingPages { get; init; } = new();
    }
}
