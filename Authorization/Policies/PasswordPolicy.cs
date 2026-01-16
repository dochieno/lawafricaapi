using System.Text.RegularExpressions;

public static class PasswordPolicy
{
    // At least 8 chars, 1 upper, 1 lower, 1 digit, 1 special
    public static (bool ok, string? error) Validate(string? password)
    {
        password ??= "";

        if (password.Length < 8)
            return (false, "Password must be at least 8 characters long.");

        if (!Regex.IsMatch(password, "[A-Z]"))
            return (false, "Password must contain at least one uppercase letter (A-Z).");

        if (!Regex.IsMatch(password, "[a-z]"))
            return (false, "Password must contain at least one lowercase letter (a-z).");

        if (!Regex.IsMatch(password, "[0-9]"))
            return (false, "Password must contain at least one number (0-9).");

        // Special = anything not letter/digit
        if (!Regex.IsMatch(password, @"[^a-zA-Z0-9]"))
            return (false, "Password must contain at least one special character (e.g. !@#$%).");

        return (true, null);
    }
}
