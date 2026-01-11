using LawAfrica.API.Models;

namespace LawAfrica.API.Models.DTOs.Institutions
{
    public class InstitutionListItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ShortName { get; set; }
        public string EmailDomain { get; set; } = string.Empty;
        public string OfficialEmail { get; set; } = string.Empty;

        public InstitutionType InstitutionType { get; set; }
        public bool RequiresUserApproval { get; set; }

        public bool IsVerified { get; set; }
        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }
        public bool AllowIndividualPurchasesWhenInstitutionInactive { get; set; }

    }

    public class InstitutionDetailsDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ShortName { get; set; }

        public string EmailDomain { get; set; } = string.Empty;
        public string OfficialEmail { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }
        public string? AlternatePhoneNumber { get; set; }

        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? StateOrProvince { get; set; }
        public string? PostalCode { get; set; }

        public int? CountryId { get; set; }
        public string? CountryName { get; set; }

        public string? RegistrationNumber { get; set; }
        public string? TaxPin { get; set; }

        public InstitutionType InstitutionType { get; set; }
        public string? InstitutionAccessCode { get; set; }
        public bool RequiresUserApproval { get; set; }

        public int? MaxStudentSeats { get; set; }
        public int? MaxStaffSeats { get; set; }

        public bool IsVerified { get; set; }
        public bool IsActive { get; set; }
        public DateTime? ActivatedAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool AllowIndividualPurchasesWhenInstitutionInactive { get; set; }

    }

    public class CreateInstitutionRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? ShortName { get; set; }

        public string EmailDomain { get; set; } = string.Empty;
        public string OfficialEmail { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }
        public string? AlternatePhoneNumber { get; set; }

        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? StateOrProvince { get; set; }
        public string? PostalCode { get; set; }

        public int? CountryId { get; set; }

        public string? RegistrationNumber { get; set; }
        public string? TaxPin { get; set; }

        public InstitutionType InstitutionType { get; set; } = InstitutionType.Academic;

        public string? InstitutionAccessCode { get; set; }
        public bool RequiresUserApproval { get; set; } = false;

        public int MaxStudentSeats { get; set; }
        public int MaxStaffSeats { get; set; }
    }

    public class UpdateInstitutionRequest : CreateInstitutionRequest
    {
        // Same fields as create, reuse for simplicity.
    }
}
