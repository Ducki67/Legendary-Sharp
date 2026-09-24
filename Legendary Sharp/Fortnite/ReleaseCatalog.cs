using System.Text;
using Legendary_Sharp.Application;
using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Fortnite;

internal sealed class ReleaseCatalog(IReadOnlyList<ReleaseEntry> entries, DateTime retrieved)
{
    public IReadOnlyList<ReleaseEntry> Entries { get; } = entries;

    public DateTime Retrieved { get; } = retrieved;

    public IReadOnlyList<string> Seasons { get; } =
        [.. entries.Select(entry => entry.Season).Distinct(StringComparer.Ordinal)];

    public IEnumerable<ReleaseEntry> Downloadable => Entries.Where(entry => entry.HasManifest);

    public static async Task<ReleaseCatalog> LoadAsync(
        ChunkSource source,
        AppPaths paths,
        TimeSpan maximumAge,
        bool forceRefresh,
        CancellationToken cancellation)
    {
        var releases = await FetchAsync(source, paths, "fn-releases.md", FortniteConstants.ReleaseIndexUrl,
            maximumAge, forceRefresh, required: true, cancellation).ConfigureAwait(false);

        var archive = await FetchAsync(source, paths, "fn-archive.md", FortniteConstants.ArchiveIndexUrl,
            maximumAge, forceRefresh, required: false, cancellation).ConfigureAwait(false);

        var catalog = Parse(releases.Text, releases.Retrieved);
        return archive.Text.Length == 0 ? catalog : catalog.Merge(Parse(archive.Text, archive.Retrieved));
    }

    private static async Task<(string Text, DateTime Retrieved)> FetchAsync(
        ChunkSource source,
        AppPaths paths,
        string fileName,
        string url,
        TimeSpan maximumAge,
        bool forceRefresh,
        bool required,
        CancellationToken cancellation)
    {
        var cache = Path.Combine(paths.Cache, fileName);
        var info = new FileInfo(cache);

        if (!forceRefresh && info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc < maximumAge)
            return (await File.ReadAllTextAsync(cache, cancellation).ConfigureAwait(false), info.LastWriteTimeUtc);

        try
        {
            var payload = await source.GetAsync(url, cancellation).ConfigureAwait(false);
            var text = Encoding.UTF8.GetString(payload);

            if (Parse(text, DateTime.UtcNow).Entries.Count == 0)
                throw new InvalidDataException("the page no longer has a release table");

            AtomicFile.WriteAllBytes(cache, payload);
            return (text, DateTime.UtcNow);
        }
        catch (Exception) when (info.Exists && !cancellation.IsCancellationRequested)
        {
            return (await File.ReadAllTextAsync(cache, cancellation).ConfigureAwait(false), info.LastWriteTimeUtc);
        }
        catch (Exception) when (!required && !cancellation.IsCancellationRequested)
        {
            return (string.Empty, DateTime.UtcNow);
        }
        catch (Exception error) when (!cancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Could not download the release index from GitHub, {error.Message.TrimEnd('.')}. Check your connection.",
                error);
        }
    }

    public ReleaseCatalog Merge(ReleaseCatalog other)
    {
        var merged = Entries.ToList();
        var byVersion = merged
            .Select((entry, index) => (entry, index))
            .ToDictionary(item => item.entry.Version, item => item.index, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in other.Entries)
        {
            if (byVersion.TryGetValue(entry.Version, out var index))
            {
                if (!merged[index].HasManifest && entry.HasManifest)
                    merged[index] = merged[index] with { ManifestId = entry.ManifestId };
                continue;
            }

            byVersion[entry.Version] = merged.Count;
            merged.Add(entry);
        }

        return new ReleaseCatalog(merged, Retrieved);
    }

    public static ReleaseCatalog Parse(string markdown, DateTime retrieved)
    {
        var entries = new List<ReleaseEntry>();
        var season = "Unknown";
        string[]? header = null;

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith('#'))
            {
                season = line.TrimStart('#').Trim();
                header = null;
                continue;
            }

            if (!line.StartsWith('|'))
            {
                header = null;
                continue;
            }

            var cells = SplitRow(line);
            if (cells.Length == 0) continue;

            if (cells.All(IsSeparator)) continue;

            if (header is null)
            {
                header = cells;
                continue;
            }

            var entry = Build(season, header, cells);
            if (entry is not null) entries.Add(entry);
        }

        return new ReleaseCatalog(entries, retrieved);
    }

    private static ReleaseEntry? Build(string season, string[] header, string[] cells)
    {
        string Column(string name)
        {
            var index = Array.FindIndex(header, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index < cells.Length ? cells[index] : string.Empty;
        }

        var version = Column("Build version");
        if (version.Length == 0) version = Naming.ShortenBuildVersion(Column("Version"));
        if (version.Length == 0 || version.Equals("unknown", StringComparison.Ordinal)) return null;

        var manifest = Column("Manifest");
        if (manifest.Length == 0) manifest = Column("Manifest ID");
        if (manifest.Contains('/') || manifest.Contains('[')) manifest = string.Empty;

        return new ReleaseEntry
        {
            Season = SeasonFor(season, version),
            Version = version,
            EngineVersion = Column("Engine version"),
            NetCl = Column("Net CL"),
            BuildDate = Column("Build date"),
            ManifestId = manifest,
            Notes = Column("Notes")
        };
    }

    private static string SeasonFor(string heading, string version)
    {
        if (heading.StartsWith("Season", StringComparison.OrdinalIgnoreCase)) return heading;

        var digits = new string(version.TakeWhile(char.IsDigit).ToArray());
        return digits.Length > 0 ? "Season " + digits : heading;
    }

    private static string[] SplitRow(string line)
    {
        var trimmed = line.Trim('|');
        var parts = trimmed.Split('|');
        for (var i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        return parts;
    }

    private static bool IsSeparator(string cell) =>
        cell.Length > 0 && cell.All(character => character is '-' or ':' or ' ');

    public IEnumerable<ReleaseEntry> Search(string? needle, string? season, bool onlyDownloadable)
    {
        var query = Entries.AsEnumerable();

        if (onlyDownloadable) query = query.Where(entry => entry.HasManifest);

        if (!string.IsNullOrWhiteSpace(season))
        {
            var wanted = ResolveSeason(season);
            query = wanted is null
                ? query.Where(entry => entry.Season.Contains(season, StringComparison.OrdinalIgnoreCase))
                : query.Where(entry => entry.Season.Equals(wanted, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(needle))
            query = query.Where(entry => entry.Matches(needle));

        return query;
    }

    public string? ResolveSeason(string needle)
    {
        var trimmed = needle.Trim();

        return Seasons.FirstOrDefault(season => season.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
               ?? Seasons.FirstOrDefault(season => season.Equals("Season " + trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public ReleaseEntry? Find(string needle)
    {
        var exact = Entries.FirstOrDefault(entry =>
            entry.Version.Equals(needle, StringComparison.OrdinalIgnoreCase) ||
            entry.ManifestId.Equals(needle, StringComparison.OrdinalIgnoreCase));

        if (exact is not null) return exact;

        var matches = Entries
            .Where(entry => entry.HasManifest && entry.Matches(needle))
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }
}
