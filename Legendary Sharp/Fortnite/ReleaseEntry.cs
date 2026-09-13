namespace Legendary_Sharp.Fortnite;

internal sealed record ReleaseEntry
{
    public required string Season { get; init; }

    public required string Version { get; init; }

    public string EngineVersion { get; init; } = string.Empty;

    public string NetCl { get; init; } = string.Empty;

    public string BuildDate { get; init; } = string.Empty;

    public string ManifestId { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public bool HasManifest => ManifestId.Length > 0;

    public bool IsVerified => VerifiedBuilds.IsVerified(Version);

    public bool IsKnownPatchy => VerifiedBuilds.IsKnownPatchy(Version);

    public string ShortVersion
    {
        get
        {
            var dash = Version.IndexOf("-CL-", StringComparison.Ordinal);
            return dash > 0 ? Version[..dash] : Version;
        }
    }

    public string Changelist
    {
        get
        {
            var marker = Version.IndexOf("-CL-", StringComparison.Ordinal);
            return marker > 0 ? Version[(marker + 4)..] : string.Empty;
        }
    }

    public string SuggestedFolderName => Version;

    public bool Matches(string needle) =>
        Version.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
        Season.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
        ManifestId.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
        Notes.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
        BuildDate.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
