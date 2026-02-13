using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Models.LawReports.Models
{
    [Index(nameof(CountryId), IsUnique = true)]
    public class CourtSequence
    {
        public int Id { get; set; }

        public int CountryId { get; set; }

        // next sequence number to issue (starts at 1)
        public int NextValue { get; set; } = 1;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
