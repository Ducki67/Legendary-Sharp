using System.Text;
using Legendary_Sharp.Application;
using Legendary_Sharp.Downloader;

namespace Legendary_Sharp.Fortnite;

internal sealed record UefnRelease(string Version, string Changelist, string ManifestId)
{
    public string Display => $"UEFN {Version} CL-{Changelist}";

    public string Name => $"UEFN {Version}-CL-{Changelist}";

    public int? MissingFiles => UefnSurvey.MissingFiles(Changelist);

    public string Status => MissingFiles switch
    {
        0 => "✓ downloads in full",
        { } missing => $"{missing:N0} files no longer hosted",
        null => "the editor on its own, no game files"
    };

    public bool Matches(string reference)
    {
        var text = reference.Trim();
        if (text.StartsWith("UEFN", StringComparison.OrdinalIgnoreCase)) text = text[4..].Trim();
        if (text.StartsWith("CL-", StringComparison.OrdinalIgnoreCase)) text = text[3..];

        return text.Equals(Version, StringComparison.OrdinalIgnoreCase) ||
               (!text.Contains('.') && Version.StartsWith(text + ".", StringComparison.Ordinal)) ||
               text.Equals(Changelist, StringComparison.Ordinal) ||
               text.Equals($"{Version}-CL-{Changelist}", StringComparison.OrdinalIgnoreCase) ||
               text.Equals($"{Version} CL-{Changelist}", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class UefnCatalog(IReadOnlyList<UefnRelease> releases)
{
    public IReadOnlyList<UefnRelease> Releases { get; } = releases;

    public static async Task<UefnCatalog> LoadAsync(
        ChunkSource source,
        AppPaths paths,
        TimeSpan maximumAge,
        bool forceRefresh,
        CancellationToken cancellation)
    {
        var cache = Path.Combine(paths.Cache, "uefn-releases.md");
        var info = new FileInfo(cache);
        var fresh = info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc < maximumAge;

        if (!forceRefresh && fresh)
            return Parse(await File.ReadAllTextAsync(cache, cancellation).ConfigureAwait(false));

        try
        {
            var payload = await source.GetAsync(FortniteConstants.UefnIndexUrl, cancellation).ConfigureAwait(false);
            var catalog = Parse(Encoding.UTF8.GetString(payload));

            if (catalog.Releases.Count == 0 && info.Exists)
                return Parse(await File.ReadAllTextAsync(cache, cancellation).ConfigureAwait(false));

            AtomicFile.WriteAllBytes(cache, payload);
            return catalog;
        }
        catch (Exception error) when (info.Exists && error is not OperationCanceledException)
        {
            return Parse(await File.ReadAllTextAsync(cache, cancellation).ConfigureAwait(false));
        }
    }

    public static UefnCatalog Parse(string markdown)
    {
        var releases = new List<UefnRelease>();

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('|')) continue;

            var cells = line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 2) continue;

            var build = cells[0];
            var manifest = cells[1];

            if (!build.StartsWith("UEFN", StringComparison.OrdinalIgnoreCase)) continue;
            if (manifest.Length == 0 || manifest.Any(char.IsWhiteSpace)) continue;

            var marker = build.IndexOf("CL-", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) continue;

            var changelist = new string(build[(marker + 3)..].TakeWhile(char.IsDigit).ToArray());
            if (changelist.Length == 0) continue;

            var version = build[4..marker].Trim();
            changelist = UefnSurvey.CorrectChangelist(manifest, changelist);
            releases.Add(new UefnRelease(version, changelist, manifest));
        }

        return new UefnCatalog(releases);
    }

    public UefnRelease? MatchChangelist(string? changelist) =>
        string.IsNullOrEmpty(changelist)
            ? null
            : Releases.FirstOrDefault(release => release.Changelist == changelist);

    public static bool IsUefnReference(string reference) =>
        reference.TrimStart().StartsWith("UEFN", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<UefnRelease> Find(string reference) =>
        [.. Releases.Where(release => release.Matches(reference))];

    public IReadOnlyList<UefnRelease> Search(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Releases
            : [.. Releases.Where(release => release.Name.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase))];

    public UefnRelease? MatchBuild(string buildVersion)
    {
        var marker = buildVersion.IndexOf("CL-", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return null;

        var changelist = new string(buildVersion[(marker + 3)..].TakeWhile(char.IsDigit).ToArray());
        return MatchChangelist(changelist);
    }
}
