namespace LawAfrica.API.Constants
{
    /// <summary>
    /// Central place to keep "special" products.
    /// Bundle is treated as a special ContentProduct that institutions subscribe to.
    /// </summary>
    public static class ProductIds
    {
        // We identify the bundle product by a stable slug/name, not a numeric ID,
        // because IDs differ per database.
        public const string InstitutionBundleProductName = "Institution All-Access Bundle";
    }
}
