namespace LawAfrica.API.Models.Payments
{
    public enum InvoiceStatus
    {
        Draft = 1,
        Issued = 2,
        PartiallyPaid = 3,
        Paid = 4,
        Void = 5
    }
}
