namespace Legendary_Sharp.Fortnite;

internal static class UefnSurvey
{
    public const string FirstComplete = "24.30-CL-25347382";

    private static readonly Dictionary<string, string> Corrections = new(StringComparer.Ordinal)
    {
        ["2kT4c-u8xpYk0ieZlEybfja5F3JY9w"] = "24903530"
    };

    private static readonly Dictionary<string, int> Missing = new(StringComparer.Ordinal)
    {
        ["24757023"] = 5529,
        ["24903530"] = 4675,
        ["24939793"] = 2107,
        ["25019967"] = 2107,
        ["25058254"] = 2107,
        ["25156858"] = 2099,
        ["25210886"] = 6,
        ["25347382"] = 0,
        ["25420516"] = 0,
        ["25521145"] = 0,
        ["25595478"] = 0,
        ["29552510"] = 0
    };

    public static int? MissingFiles(string changelist) =>
        Missing.TryGetValue(changelist, out var missing) ? missing : null;

    public static string CorrectChangelist(string manifestId, string changelist) =>
        Corrections.TryGetValue(manifestId, out var corrected) ? corrected : changelist;
}
