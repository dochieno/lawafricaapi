namespace LawAfrica.API.DTOs.AI
{
    public class LawReportChatRequestDto
    {
        public string Message { get; set; } = "";
        public List<LawReportChatTurnDto>? History { get; set; } // optional
    }
}