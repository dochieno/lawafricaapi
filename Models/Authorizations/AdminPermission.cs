namespace LawAfrica.API.Models.Authorization
{
    public class AdminPermission
    {
        public int Id { get; set; }

        // e.g. "records.delete", "users.approve"
        public string Code { get; set; } = string.Empty;

        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
