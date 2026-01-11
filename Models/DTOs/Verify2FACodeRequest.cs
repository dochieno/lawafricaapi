namespace LawAfrica.API.Models.DTOs
{
    public class Verify2FACodeRequest
    {
        public int UserId { get; set; }
        public string Code { get; set; } = string.Empty;
    }
}
