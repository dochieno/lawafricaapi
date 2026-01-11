namespace LawAfrica.API.Models.DTOs
{
        public record UpdateReadingProgressRequest(
        int? PageNumber,        // PDF
        string? Cfi,            // EPUB
        int? CharOffset,        // HTML/Text (future)
        double Percentage,      // 0..100
        int SecondsReadDelta,   // time since last update
        bool? IsCompleted
    );
}
