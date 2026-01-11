using LawAfrica.API.Models;

namespace LawAfrica.API.Models.DTOs.Products
{
    /// <summary>
    /// Used by admins to create content products via Swagger / Admin UI.
    /// </summary>
    public class CreateContentProductRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        /// <summary>
        /// Legacy input (optional).
        /// If older UI/clients send this, server maps it to PublicAccessModel.
        /// 
        /// IMPORTANT:
        /// Default is Unknown so it does NOT override PublicAccessModel unless explicitly set.
        /// </summary>
        public ProductAccessModel AccessModel { get; set; } = ProductAccessModel.Unknown;

        /// <summary>
        /// Audience-specific access model for institutions.
        /// Default: Subscription.
        /// </summary>
        public ProductAccessModel InstitutionAccessModel { get; set; } = ProductAccessModel.Subscription;

        /// <summary>
        /// Audience-specific access model for public/individuals.
        /// Default: OneTimePurchase.
        /// </summary>
        public ProductAccessModel PublicAccessModel { get; set; } = ProductAccessModel.OneTimePurchase;

        /// <summary>
        /// Bundle inclusion flags.
        /// - Most products included in institution bundle by default.
        /// - Public bundle not used now, default false.
        /// 
        /// Note: these are only meaningful if the corresponding access model is Subscription.
        /// </summary>
        public bool IncludedInInstitutionBundle { get; set; } = true;
        public bool IncludedInPublicBundle { get; set; } = false;

        public bool AvailableToInstitutions { get; set; } = true;
        public bool AvailableToPublic { get; set; } = true;
    }
}
