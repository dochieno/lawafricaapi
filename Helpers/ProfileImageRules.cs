public static class ProfileImageRules
{
    public const int MaxFileSizeBytes = 2 * 1024 * 1024; // 2MB

    public static readonly string[] AllowedTypes =
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };
}
