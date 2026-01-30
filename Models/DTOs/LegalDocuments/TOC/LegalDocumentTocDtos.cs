using System.ComponentModel.DataAnnotations;
using LawAfrica.API.Models.Documents;

namespace LawAfrica.API.Models.DTOs.LegalDocuments.Toc
{
    public class TocEntryDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }

        public string Title { get; set; } = string.Empty;
        public TocEntryLevel Level { get; set; }
        public int Order { get; set; }

        public TocTargetType TargetType { get; set; }
        public int? StartPage { get; set; }
        public int? EndPage { get; set; }
        public string? AnchorId { get; set; }

        public string? PageLabel { get; set; }

        // Admin-only fields (only returned in admin endpoints)
        public string? Notes { get; set; }

        public List<TocEntryDto> Children { get; set; } = new();
    }

    public class TocEntryCreateRequest
    {
        public int? ParentId { get; set; }

        [Required, MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        public TocEntryLevel Level { get; set; } = TocEntryLevel.Section;

        // Optional: if not provided, we append to end in that parent
        public int? Order { get; set; }

        public TocTargetType TargetType { get; set; } = TocTargetType.PageRange;

        public int? StartPage { get; set; }
        public int? EndPage { get; set; }

        [MaxLength(200)]
        public string? AnchorId { get; set; }

        [MaxLength(50)]
        public string? PageLabel { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }
    }

    public class TocEntryUpdateRequest
    {
        [Required, MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        public TocEntryLevel Level { get; set; } = TocEntryLevel.Section;

        public TocTargetType TargetType { get; set; } = TocTargetType.PageRange;

        public int? StartPage { get; set; }
        public int? EndPage { get; set; }

        [MaxLength(200)]
        public string? AnchorId { get; set; }

        [MaxLength(50)]
        public string? PageLabel { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }
    }

    public class TocReorderItemRequest
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public int Order { get; set; }
    }

    public class TocReorderRequest
    {
        public List<TocReorderItemRequest> Items { get; set; } = new();
    }

    // Bulk upload/import (supports "replace" and "append")
    public class TocImportRequest
    {
        // "replace" | "append"
        [Required]
        public string Mode { get; set; } = "replace";

        public List<TocImportItem> Items { get; set; } = new();
    }

    public class TocImportItem
    {
        // Client-side temp ids for building hierarchy in one payload
        public string ClientId { get; set; } = Guid.NewGuid().ToString("N");
        public string? ParentClientId { get; set; }

        [Required, MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        public TocEntryLevel Level { get; set; } = TocEntryLevel.Section;
        public int Order { get; set; }

        public TocTargetType TargetType { get; set; } = TocTargetType.PageRange;
        public int? StartPage { get; set; }
        public int? EndPage { get; set; }

        [MaxLength(200)]
        public string? AnchorId { get; set; }

        [MaxLength(50)]
        public string? PageLabel { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }
    }
}
