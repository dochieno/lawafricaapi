namespace LawAfrica.API.Services.Ai.Sections
{
    public interface ISectionTextExtractor
    {
        Task<SectionTextExtractionResult> ExtractAsync(
            int legalDocumentId,
            int startPage,
            int endPage,
            CancellationToken ct);
    }
}
