using System.Text.RegularExpressions;

namespace LawAfrica.API.DTOs.Reports
{
    public static class ReportValidation
    {
        // ✅ Must start with 3 letters + digits. Example: CAR353
        // (If you need to allow suffix later, we can adjust.)
        public static readonly Regex ReportNumberRegex = new(@"^[A-Za-z]{3}\d+$", RegexOptions.Compiled);

        public static bool IsValidReportNumber(string? value)
            => !string.IsNullOrWhiteSpace(value) && ReportNumberRegex.IsMatch(value.Trim());
    }
}
