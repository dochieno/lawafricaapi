namespace LawAfrica.API.Services.Ai.Commentary
{
    public class UserJurisdictionContext
    {
        public int? CountryId { get; set; }
        public string CountryName { get; set; } = "";
        public string CountryIso { get; set; } = "";
        public string RegionLabel { get; set; } = ""; // e.g. "East Africa"
        public List<int> RegionCountryIds { get; set; } = new();
        public List<int> AfricaCountryIds { get; set; } = new();
        // ✅ user identity for nicer responses
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DisplayName { get; set; }    // convenience

        public string? City { get; set; }
    }

    public interface IUserJurisdictionResolver
    {
        Task<UserJurisdictionContext> ResolveAsync(int userId, CancellationToken ct);
    }
}
