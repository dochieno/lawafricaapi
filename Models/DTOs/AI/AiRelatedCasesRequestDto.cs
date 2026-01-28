namespace LawAfrica.API.DTOs.Ai
{
    public class AiRelatedCasesRequestDto
    {
        public int TakeKenya { get; set; } = 6;
        public int TakeForeign { get; set; } = 2; // outside Kenya for reference
        public bool ForceRegenerate { get; set; } = false; // future: caching control
    }
}