namespace LawAfrica.API.DTOs.AI
{
    public class LawReportChatTurnDto
    {
        public string Role { get; set; } = "user"; // "user" | "assistant"
        public string Content { get; set; } = "";
    }
}