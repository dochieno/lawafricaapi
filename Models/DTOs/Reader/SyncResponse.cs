namespace LawAfrica.API.Models.DTOs
{
    public record SyncResponse(
        DateTime ServerTime,
        List<SyncProgressItem> ServerProgress,
        List<SyncAnnotationItem> ServerAnnotations
    );
}
