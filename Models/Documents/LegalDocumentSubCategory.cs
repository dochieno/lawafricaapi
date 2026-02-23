using LawAfrica.API.Models;

namespace LawAfrica.API.Models.Documents
{
    public class LegalDocumentSubCategory
    {
        public int Id { get; set; }

        // Parent category (enum stored as int)
        public LegalDocumentCategory Category { get; set; } = LegalDocumentCategory.Statutes;

        public string Name { get; set; } = string.Empty;   // e.g. "Constitutional Statutes"
        public string Code { get; set; } = string.Empty;   // e.g. "constitutional_statutes"

        public int SortOrder { get; set; } = 0;
        public bool IsActive { get; set; } = true;

        // Optional future scope:
        public int? CountryId { get; set; }
        public Country? Country { get; set; }
    }
}