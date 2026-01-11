namespace LawAfrica.API.Models.DTOs
{
    public class Verify2FARequest
    {
        public string Username { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }
}
