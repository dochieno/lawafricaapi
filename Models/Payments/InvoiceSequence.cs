namespace LawAfrica.API.Models.Payments
{
    /// <summary>
    /// Stores per-year invoice sequence for serialized, concurrency-safe invoice numbering.
    /// </summary>
    public class InvoiceSequence
    {
        public int Id { get; set; }

        // e.g. 2026
        public int Year { get; set; }

        // last issued number for the year
        public long LastNumber { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
