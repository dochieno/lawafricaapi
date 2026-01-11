namespace LawAfrica.API.Models.Emails
{
    public class EmailInlineImage
    {
        public string ContentId { get; set; } = string.Empty; // e.g. "qrcode"
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "image/png";
        public string? FileName { get; set; } = null;
    }
}
