using System.Text.RegularExpressions;

namespace ComplaintSystem.Application.Security;

public static partial class UniversityEmailPolicy
{
    [GeneratedRegex("^(?:ma|ce|gl|gm|cv|mc|el|ds|es|tp)-[a-z0-9._%+]+@st\\.umat\\.edu\\.gh$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StudentEmailRegex();

    [GeneratedRegex("^[a-z0-9._%+-]+@umat\\.edu\\.gh$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StaffEmailRegex();

    [GeneratedRegex("^901\\d{5}(?<year>\\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegex();

    public static bool IsApprovedStudentEmail(string email) => StudentEmailRegex().IsMatch(email.Trim());
    public static bool IsApprovedStaffEmail(string email) => StaffEmailRegex().IsMatch(email.Trim());
    public static bool IsValidReferenceNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = ReferenceRegex().Match(value.Trim());
        return match.Success && int.TryParse(match.Groups["year"].Value, out var year) && year >= 22;
    }
    public static string NormalizeReferenceNumber(string value) => value.Trim().ToUpperInvariant();
}
