namespace LawAfrica.API.Models.DTOs.AI
{
    public class GenerateSummaryRequestDto
    {
        public string Type { get; set; } = "basic"; // "basic" | "extended"
        public bool ForceRegenerate { get; set; } = false;
    }
}