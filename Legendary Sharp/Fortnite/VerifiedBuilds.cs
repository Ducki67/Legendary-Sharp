namespace Legendary_Sharp.Fortnite;

internal static class VerifiedBuilds
{
    private const int ModernMajor = 34;
    private const int ModernMinor = 10;

    private static readonly HashSet<string> Changelists = new(StringComparer.Ordinal)
    {
        "40567068"
    };

    public static bool IsVerified(string version)
    {
        if (Survey(version) is { } missing) return missing.All(count => count == 0);

        var changelist = Changelist(version);
        if (changelist.Length > 0 && Changelists.Contains(changelist)) return true;

        var (major, minor) = Parse(version);
        return major > ModernMajor || (major == ModernMajor && minor >= ModernMinor);
    }

    public static bool IsKnownPatchy(string version)
    {
        if (Survey(version) is { } missing) return missing.Any(count => count > 0);
        if (IsVerified(version) || IsOtherPlatform(version)) return false;

        var (major, minor) = Parse(version);
        return major > 0 && (major < 13 || (major == 13 && minor < 40));
    }

    public static string? MissingNote(string version)
    {
        if (Survey(version) is not { } missing || missing.All(count => count == 0)) return null;

        var (fewest, most) = (missing.Min(), missing.Max());
        return fewest == most ? $"{Files(most)} missing" : $"{fewest} to {Files(most)} missing";
    }

    public static int? MissingFor(string version, string preset) => BuildSurvey.MissingFor(SurveyKey(version), preset);

    public static bool IsUnavailable(string version) => BuildSurvey.IsUnavailable(SurveyKey(version));

    private static int[]? Survey(string version) => BuildSurvey.Find(SurveyKey(version));

    private static string SurveyKey(string version)
    {
        var marker = version.IndexOf("-CL-", StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? string.Empty : version[(marker + 4)..];
    }

    private static string Files(int files) => files == 1 ? "1 file" : $"{files} files";

    public static bool IsOtherPlatform(string version)
    {
        var changelist = Changelist(version);
        if (changelist.Length == 0) return false;

        var end = version.IndexOf(changelist, StringComparison.Ordinal) + changelist.Length;
        return version[end..].Any(char.IsLetter);
    }

    private static string Changelist(string version)
    {
        var marker = version.IndexOf("CL-", StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? string.Empty : new string(version[(marker + 3)..].TakeWhile(char.IsDigit).ToArray());
    }

    private static (int Major, int Minor) Parse(string version)
    {
        var major = new string(version.TakeWhile(char.IsDigit).ToArray());
        if (major.Length == 0) return (0, 0);

        var rest = version[major.Length..];
        if (rest.Length == 0 || rest[0] != '.') return (int.Parse(major), 0);

        var minor = new string(rest[1..].TakeWhile(char.IsDigit).ToArray());
        return (int.Parse(major), minor.Length == 0 ? 0 : int.Parse(minor));
    }
}
