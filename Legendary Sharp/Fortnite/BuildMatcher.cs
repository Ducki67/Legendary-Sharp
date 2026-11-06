using Legendary_Sharp.Application;
using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Fortnite;

internal sealed record TagFinding(TagGroup Group, int Present, int Total);

internal sealed record BuildMatch(
    ResolvedManifest Source,
    ReleaseEntry? Release,
    int Present,
    int Total,
    bool ExeMatches,
    IReadOnlyList<TagFinding> Tags,
    IReadOnlyList<string> SuggestedTags,
    bool Accepted);

internal static class BuildMatcher
{
    private const int MaxCandidates = 6;
    private const double GroupThreshold = 0.5;
    internal const double AcceptThreshold = 0.9;

    public static async Task<IReadOnlyList<BuildMatch>> MatchAsync(
        Session session,
        string root,
        BuildProbe probe,
        CancellationToken cancellation)
    {
        var catalog = await session.CatalogAsync(false, cancellation).ConfigureAwait(false);
        var sources = new List<ResolvedManifest>();

        if (probe.EgstoreManifest is { Length: > 0 } stored)
        {
            try
            {
                var manifest = await ManifestLoader.LoadFileAsync(stored, cancellation).ConfigureAwait(false);
                var release = probe.Changelist is null
                    ? null
                    : catalog.Entries.FirstOrDefault(entry => entry.HasManifest && entry.Changelist == probe.Changelist);
                var id = release?.ManifestId ?? Path.GetFileNameWithoutExtension(stored);
                sources.Add(new ResolvedManifest(manifest, id, stored, release));
            }
            catch (Exception error) when (!cancellation.IsCancellationRequested && error is not OutOfMemoryException)
            {
            }
        }

        if (sources.Count == 0)
        {
            var entries = catalog.Entries
                .Where(entry => entry.HasManifest && !VerifiedBuilds.IsOtherPlatform(entry.Version))
                .Where(entry => probe.Changelist is not null
                    ? entry.Changelist == probe.Changelist
                    : entry.ShortVersion.Equals(probe.Version, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(entry => entry.ManifestId)
                .OrderByDescending(entry => long.TryParse(entry.Changelist, out var cl) ? cl : 0)
                .Take(MaxCandidates);

            foreach (var entry in entries)
            {
                try
                {
                    sources.Add(await ManifestResolver
                        .ResolveAsync(entry.ManifestId, session.Http, session.Paths, catalog, cancellation)
                        .ConfigureAwait(false));
                }
                catch (Exception error) when (!cancellation.IsCancellationRequested && error is not OutOfMemoryException)
                {
                }
            }
        }

        var exe = new FileInfo(Path.Combine(root, FortniteConstants.BinariesFolder, FortniteConstants.ShippingExecutable));
        var exeLength = exe.Exists ? exe.Length : -1;
        var lengths = new Dictionary<string, long>(StringComparer.Ordinal);

        var matches = new List<BuildMatch>();
        foreach (var source in sources)
        {
            cancellation.ThrowIfCancellationRequested();
            matches.Add(Evaluate(source, root, exeLength, lengths));
        }

        return [.. matches
            .OrderByDescending(match => match.Accepted)
            .ThenByDescending(match => match.ExeMatches)
            .ThenByDescending(match => match.Total == 0 ? 0d : (double)match.Present / match.Total)
            .ThenByDescending(match => match.Release?.IsVerified ?? false)];
    }

    private static BuildMatch Evaluate(
        ResolvedManifest source, string root, long exeLength, Dictionary<string, long> lengths)
    {
        var manifest = source.Manifest;
        var files = manifest.Files;
        var onDisk = new bool[files.Count];
        var present = 0;

        for (var index = 0; index < files.Count; index++)
        {
            if (DiskLength(root, files[index], lengths) != files[index].Size) continue;
            onDisk[index] = true;
            present++;
        }

        var exeMatches = false;
        if (exeLength >= 0)
        {
            exeMatches = files.Any(file =>
                file.Size == exeLength &&
                file.FileName.Replace('\\', '/').EndsWith(
                    FortniteConstants.ShippingManifestSuffix,
                    StringComparison.OrdinalIgnoreCase));
        }

        var findings = new List<TagFinding>();
        var suggested = new List<string> { string.Empty };
        var suggestedSet = new HashSet<string> { string.Empty };
        var chosenGroups = 0;

        foreach (var group in InstallTagCatalog.Summarise(manifest))
        {
            var total = 0;
            var found = 0;
            for (var index = 0; index < files.Count; index++)
            {
                if (!DownloadPlanner.Matches(files[index], group.Tags)) continue;
                total++;
                if (onDisk[index]) found++;
            }

            if (total == 0) continue;
            findings.Add(new TagFinding(group, found, total));

            if ((double)found / total < GroupThreshold) continue;
            foreach (var tag in group.Tags)
                if (suggestedSet.Add(tag)) suggested.Add(tag);
            if (group.Tags.Any(tag => tag.Length > 0)) chosenGroups++;
        }

        var coversAll = manifest.InstallTags.All(suggestedSet.Contains);
        IReadOnlyList<string> suggestion = coversAll ? [] : suggested;

        var selectedTotal = 0;
        var selectedPresent = 0;
        for (var index = 0; index < files.Count; index++)
        {
            if (!coversAll && !DownloadPlanner.Matches(files[index], suggested)) continue;
            selectedTotal++;
            if (onDisk[index]) selectedPresent++;
        }

        var accepted = exeLength >= 0 &&
                       chosenGroups > 0 &&
                       selectedTotal > 0 &&
                       (double)selectedPresent / selectedTotal >= AcceptThreshold;

        return new BuildMatch(source, source.Release, present, files.Count, exeMatches, findings, suggestion, accepted);
    }

    private static long DiskLength(string root, FileEntry file, Dictionary<string, long> lengths)
    {
        if (lengths.TryGetValue(file.FileName, out var cached)) return cached;

        long length = -1;
        try
        {
            var info = new FileInfo(Path.Combine(root, file.ToNativePath()));
            if (info.Exists) length = info.Length;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                                          or NotSupportedException)
        {
        }

        lengths[file.FileName] = length;
        return length;
    }
}
