namespace LawAfrica.API.Models
{
    /// <summary>
    /// Records sensitive approval and governance actions
    /// for accountability and legal compliance.
    /// </summary>
    public class AuditEvent
    {
        public int Id { get; set; }
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public int EntityId { get; set; }
        public int PerformedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
