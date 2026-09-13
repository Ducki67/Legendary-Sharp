using System.Runtime.InteropServices;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal static class DownloadPlanner
{
    public static DownloadPlan Create(
        BuildManifest manifest,
        IReadOnlyList<string>? tags,
        IReadOnlySet<string>? alreadyComplete = null)
    {
        var selected = SelectFiles(manifest, tags);
        var selectedInstallSize = selected.Sum(file => file.Size);

        var pending = alreadyComplete is null or { Count: 0 }
            ? selected
            : [.. selected.Where(file => !alreadyComplete.Contains(file.FileName))];

        return Build(manifest, pending, tags ?? [], manifest.Files.Count - selected.Count,
            selected.Count - pending.Count, selectedInstallSize);
    }

    public static DownloadPlan CreateForFiles(
        BuildManifest manifest,
        IReadOnlyList<FileEntry> files,
        IReadOnlyList<string> tags)
    {
        var ordered = files.ToList();
        ordered.Sort(static (left, right) => string.CompareOrdinal(left.FileName, right.FileName));
        return Build(manifest, ordered, tags, 0, 0, ordered.Sum(file => file.Size));
    }

    public static List<FileEntry> SelectFiles(BuildManifest manifest, IReadOnlyList<string>? tags)
    {
        var files = tags is null
            ? manifest.Files.ToList()
            : [.. manifest.Files.Where(file => Matches(file, tags))];

        files.Sort(static (left, right) => string.CompareOrdinal(left.FileName, right.FileName));
        return files;
    }

    public static bool Matches(FileEntry file, IReadOnlyList<string> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Length == 0)
            {
                if (file.InstallTags.Length == 0) return true;
                continue;
            }

            if (Array.IndexOf(file.InstallTags, tag) >= 0) return true;
        }

        return false;
    }

    private static DownloadPlan Build(
        BuildManifest manifest,
        List<FileEntry> pending,
        IReadOnlyList<string> tags,
        int skippedByTag,
        int alreadyComplete,
        long selectedInstallSize)
    {
        var order = new List<ChunkGuid>();
        var references = new Dictionary<ChunkGuid, int>();

        foreach (var part in pending.SelectMany(file => file.Parts))
        {
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(references, part.Guid, out var existed);
            count++;
            if (!existed) order.Add(part.Guid);
        }

        return new DownloadPlan
        {
            Manifest = manifest,
            Files = pending,
            ChunkOrder = order,
            References = references,
            SelectedTags = tags,
            SkippedByTag = skippedByTag,
            AlreadyComplete = alreadyComplete,
            DownloadSize = order.Sum(guid => manifest.Chunk(guid).CompressedSize),
            WriteSize = pending.Sum(file => file.Size),
            SelectedInstallSize = selectedInstallSize,
            PeakCacheBytes = PeakCacheBytes(manifest, pending, references)
        };
    }

    private static long PeakCacheBytes(
        BuildManifest manifest,
        IReadOnlyList<FileEntry> files,
        IReadOnlyDictionary<ChunkGuid, int> references)
    {
        var remaining = new Dictionary<ChunkGuid, int>(references);
        var resident = new HashSet<ChunkGuid>();
        long live = 0;
        long peak = 0;

        foreach (var part in files.SelectMany(file => file.Parts))
        {
            var size = manifest.Chunk(part.Guid).WindowSize;

            if (resident.Add(part.Guid))
            {
                live += size;
                if (live > peak) peak = live;
            }

            ref var count = ref CollectionsMarshal.GetValueRefOrNullRef(remaining, part.Guid);
            if (--count > 0) continue;

            resident.Remove(part.Guid);
            live -= size;
        }

        return peak;
    }
}
