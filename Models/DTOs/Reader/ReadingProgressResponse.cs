namespace LawAfrica.API.Models.DTOs
{
    public record ReadingProgressResponse(
        int DocumentId,
        int? PageNumber,
        string? Cfi,
        int? CharOffset,
        double Percentage,
        bool IsCompleted,
        int TotalSecondsRead,
        DateTime LastReadAt
    );
}
