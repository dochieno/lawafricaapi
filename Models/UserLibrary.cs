namespace LawAfrica.API.Models
{
    public class UserLibrary
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int LegalDocumentId { get; set; }

        // ✅ EF Core navigation property
        public LegalDocument LegalDocument { get; set; } = null!;

        public LibraryAccessType AccessType { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
