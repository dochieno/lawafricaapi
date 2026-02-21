namespace LawAfrica.API.Services.Emails
{
    public class EmailOutboxOptions
    {
        public bool Enabled { get; set; } = false;
        public int PollSeconds { get; set; } = 30;
    }
}