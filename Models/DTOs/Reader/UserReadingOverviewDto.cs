namespace LawAfrica.API.Models.DTOs
{
    public record UserReadingOverviewDto(
        int TotalDocumentsRead,
        int CompletedDocuments,
        int TotalReadingTimeSeconds,
        int CurrentStreakDays
    );
}
