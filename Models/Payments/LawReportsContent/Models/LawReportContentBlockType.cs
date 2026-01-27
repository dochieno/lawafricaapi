namespace LawAfrica.API.Models.Payments.LawReportsContent.Models
{
    // Stored as smallint (see your EF config)
    public enum LawReportContentBlockType : short
    {
        Unknown = 0,

        // “Lexis-style” structure
        Title = 10,        // e.g., "Odunodo v Africa Merchant..."
        MetaLine = 20,     // e.g., "HIGH COURT OF KENYA AT KISUMU"
        Heading = 30,      // e.g., "RULING", "JUDGMENT"
        Paragraph = 40,    // normal body text

        ListItem = 50,     // numbered or bullet item
        Quote = 60,        // optional
        Divider = 70,      // optional horizontal rule
        Spacer = 80        // optional blank line spacing
    }
}