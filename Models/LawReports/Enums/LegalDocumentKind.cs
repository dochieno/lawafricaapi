namespace LawAfrica.API.Models.LawReports.Enums
{
    public enum LegalDocumentKind
    {
        Standard = 1, // existing PDFs/EPUBs (books, statutes, journals, etc.)
        Report = 2    // subscription-only law report (text-based)
    }
}
