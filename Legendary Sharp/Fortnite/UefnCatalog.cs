using System.Text;
using Legendary_Sharp.Application;
using Legendary_Sharp.Downloader;

namespace Legendary_Sharp.Fortnite;

internal sealed record UefnRelease(string Version, string Changelist, string ManifestId)
{
    public string Display => $"UEFN {Version} CL-{Changelist}";
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
            var text = Encoding.UTF8.GetString(payload);
            Directory.CreateDirectory(paths.Cache);
            await File.WriteAllTextAsync(cache, text, cancellation).ConfigureAwait(false);
            return Parse(text);
        }
        catch (Exception) when (info.Exists)
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
            releases.Add(new UefnRelease(version, changelist, manifest));
        }

        return new UefnCatalog(releases);
    }

    public UefnRelease? MatchChangelist(string? changelist) =>
        string.IsNullOrEmpty(changelist)
            ? null
            : Releases.FirstOrDefault(release => release.Changelist == changelist);

    public UefnRelease? MatchBuild(string buildVersion)
    {
        var marker = buildVersion.IndexOf("CL-", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return null;

        var changelist = new string(buildVersion[(marker + 3)..].TakeWhile(char.IsDigit).ToArray());
        return MatchChangelist(changelist);
    }
}
