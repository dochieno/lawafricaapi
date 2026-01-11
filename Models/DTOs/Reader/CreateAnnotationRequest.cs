namespace LawAfrica.API.Models.DTOs
{
    public record CreateAnnotationRequest(
        string Type,               // "highlight" | "note"
        int? PageNumber,
        int? StartCharOffset,
        int? EndCharOffset,
        string? SelectedText,
        string? Note,
        string? Color
    );
}
