namespace LawAfrica.API.Models
{
    public class Role
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // Navigation
        public ICollection<User>? Users { get; set; }
    }
}
