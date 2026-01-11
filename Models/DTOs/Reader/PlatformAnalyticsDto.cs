namespace LawAfrica.API.Models.DTOs
{
    public record PlatformAnalyticsDto(
        int TotalDocuments,
        int TotalReaders,
        int TotalAnnotations,
        int TotalReadingTimeSeconds
    );
}
