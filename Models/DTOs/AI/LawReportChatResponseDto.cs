namespace LawAfrica.API.DTOs.AI
{
    public class LawReportChatResponseDto
    {
        public int LawReportId { get; set; }
        public string Reply { get; set; } = "";
        public string Model { get; set; } = "";
        public string Disclaimer { get; set; } =
            "AI answers may be inaccurate. Always verify against the full case text.";
    }
}