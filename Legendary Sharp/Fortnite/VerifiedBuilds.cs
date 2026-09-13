namespace Legendary_Sharp.Fortnite;

internal static class VerifiedBuilds
{
    private const int ModernMajor = 34;
    private const int ModernMinor = 10;

    private static readonly HashSet<string> Changelists = new(StringComparer.Ordinal)
    {
        "14113327", "14835335", "15685441", "17468642", "17661844",
        "17745267", "17792290", "17811397", "17882303",
        "20696680", "21035704", "20978394", "21657658", "22600409", "23070899",
        "40567068"
    };

    public static bool IsVerified(string version)
    {
        var changelist = Changelist(version);
        if (changelist.Length > 0 && Changelists.Contains(changelist)) return true;

        var (major, minor) = Parse(version);
        return major > ModernMajor || (major == ModernMajor && minor >= ModernMinor);
    }

    public static bool IsKnownPatchy(string version)
    {
        if (IsVerified(version)) return false;

        var (major, minor) = Parse(version);
        if (major <= 0) return false;

        return major < 13 || (major == 13 && minor < 40);
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
