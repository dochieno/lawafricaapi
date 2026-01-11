namespace LawAfrica.API.Models.DTOs
{
    public record SyncRequest(
        DateTime LastSyncAt,
        List<SyncProgressItem> ProgressUpdates,
        List<SyncAnnotationItem> AnnotationUpdates
    );

    public record SyncProgressItem(
        int LegalDocumentId,
        int? PageNumber,
        double Percentage,
        DateTime UpdatedAt
    );

    public record SyncAnnotationItem(
        Guid ClientId,
        int LegalDocumentId,
        string Type,
        int? PageNumber,
        int? StartCharOffset,
        int? EndCharOffset,
        string? SelectedText,
        string? Note,
        string? Color,
        DateTime UpdatedAt
    );
}

