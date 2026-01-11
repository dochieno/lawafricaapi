using System;

namespace LawAfrica.API.Models
{
    /// <summary>
    /// Represents a sellable content product.
    /// A product may include one or many legal documents.
    ///
    /// NOTE:
    /// Access model differs by audience:
    /// - Institutions may access via subscription bundle or separate subscription
    /// - Public/individuals may access via one-time purchase or subscription
    /// </summary>
    public class ContentProduct
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        /// <summary>
        /// Legacy field (kept for backward compatibility).
        /// We will mirror this to PublicAccessModel.
        /// </summary>
        public ProductAccessModel AccessModel { get; set; }

        /// <summary>
        /// New: how INSTITUTIONS access this product (OneTimePurchase / Subscription).
        /// </summary>
        public ProductAccessModel InstitutionAccessModel { get; set; } = ProductAccessModel.Subscription;

        /// <summary>
        /// New: how PUBLIC/INDIVIDUALS access this product (OneTimePurchase / Subscription).
        /// </summary>
        public ProductAccessModel PublicAccessModel { get; set; } = ProductAccessModel.OneTimePurchase;

        /// <summary>
        /// If true, product is included when an institution has the "bundle" subscription.
        /// If false, product requires a separate institution subscription (e.g., Law Reports).
        /// </summary>
        public bool IncludedInInstitutionBundle { get; set; } = true;

        /// <summary>
        /// For future-proofing (e.g. if you introduce "Public bundle").
        /// If false, product always requires separate public subscription/purchase.
        /// </summary>
        public bool IncludedInPublicBundle { get; set; } = false;

        public bool AvailableToInstitutions { get; set; } = true;
        public bool AvailableToPublic { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ContentProductLegalDocument> ProductDocuments { get; set; }  = new List<ContentProductLegalDocument>();

    }
}
