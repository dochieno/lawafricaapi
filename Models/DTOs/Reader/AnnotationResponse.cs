namespace LawAfrica.API.Models.DTOs
{
    public record AnnotationResponse(
        int Id,
        string Type,
        int? PageNumber,
        int? StartCharOffset,
        int? EndCharOffset,
        string? SelectedText,
        string? Note,
        string? Color,
        DateTime CreatedAt
    );
}
