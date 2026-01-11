namespace LawAfrica.API.Models.DTOs
{
    public record UserDocumentAnalyticsDto(
        int DocumentId,
        string Title,
        double PercentageCompleted,
        int TotalSecondsRead,
        bool IsCompleted,
        DateTime LastReadAt
    );
}
