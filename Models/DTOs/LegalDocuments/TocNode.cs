namespace LawAfrica.API.Models
{
    public class TocNode
    {
        public string Title { get; set; } = "";
        public int Page { get; set; }
        public List<TocNode> Children { get; set; } = new();
    }
}
