using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal sealed class DownloadPlan
{
    public required BuildManifest Manifest { get; init; }

    public required IReadOnlyList<FileEntry> Files { get; init; }

    public required IReadOnlyList<ChunkGuid> ChunkOrder { get; init; }

    public required IReadOnlyDictionary<ChunkGuid, int> References { get; init; }

    public required IReadOnlyList<string> SelectedTags { get; init; }

    public required int SkippedByTag { get; init; }

    public required int AlreadyComplete { get; init; }

    public required long DownloadSize { get; init; }

    public required long WriteSize { get; init; }

    public required long SelectedInstallSize { get; init; }

    public required long PeakCacheBytes { get; init; }

    public int ChunkCount => ChunkOrder.Count;

    public bool IsEmpty => Files.Count == 0;
}
