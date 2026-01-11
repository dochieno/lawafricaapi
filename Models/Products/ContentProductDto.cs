using System;
using LawAfrica.API.Models;

namespace LawAfrica.API.Models.DTOs.Products
{
    /// <summary>
    /// API response DTO for content products.
    /// </summary>
    public class ContentProductDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        /// <summary>
        /// Legacy output: mirrors PublicAccessModel.
        /// </summary>
        public ProductAccessModel AccessModel { get; set; }

        /// <summary>
        /// Audience-specific models.
        /// </summary>
        public ProductAccessModel InstitutionAccessModel { get; set; }
        public ProductAccessModel PublicAccessModel { get; set; }

        /// <summary>
        /// Bundle inclusion flags.
        /// </summary>
        public bool IncludedInInstitutionBundle { get; set; }
        public bool IncludedInPublicBundle { get; set; }

        public bool AvailableToInstitutions { get; set; }
        public bool AvailableToPublic { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
