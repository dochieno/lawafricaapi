using LawAfrica.API.Models;

namespace LawAfrica.API.Models.Documents
{
    public class LegalDocumentCategoryMeta
    {
        // IMPORTANT: This Id MUST match the enum int value.
        // e.g. Statutes=5 => row with Id=5
        public int Id { get; set; }

        // Optional but strongly recommended
        public string Code { get; set; } = string.Empty;   // e.g. "statutes", "gazette"
        public string Name { get; set; } = string.Empty;   // e.g. "Statutes", "Gazette"

        public int SortOrder { get; set; } = 0;
        public bool IsActive { get; set; } = true;

        // Optional extras (add now or later)
        public string? Description { get; set; }
    }
}