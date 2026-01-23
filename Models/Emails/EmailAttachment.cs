namespace LawAfrica.API.Models.Emails
{
    public class EmailAttachment
    {
        public string FileName { get; set; } = "attachment.bin";
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "application/octet-stream";

        // Optional for future: you can support inline attachments too
        public bool IsInline { get; set; } = false;
        public string? ContentId { get; set; } = null;
    }
}
