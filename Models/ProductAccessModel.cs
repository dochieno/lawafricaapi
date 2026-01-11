namespace LawAfrica.API.Models
{
    /// <summary>
    /// Defines how a content product is accessed.
    /// </summary>
    public enum ProductAccessModel
    {
        Unknown = 0,
        OneTimePurchase = 1,   // Buy once, own forever
        Subscription = 2       // Access while subscription is active
    }
}
