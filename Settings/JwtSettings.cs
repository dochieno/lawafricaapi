namespace LawAfrica.API;

public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    // ✅ This is what your token generator should use
    public int DurationInMinutes { get; set; } = 60;
}
