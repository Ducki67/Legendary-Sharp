namespace Legendary_Sharp.Downloader;

internal sealed record DownloadOptions
{
    public required string InstallRoot { get; init; }

    public required IReadOnlyList<string> Mirrors { get; init; }

    public int Workers { get; init; } = DefaultWorkers();

    public long CacheBudgetBytes { get; init; } = 1024L * 1024 * 1024;

    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int Attempts { get; init; } = 10;

    public bool SkipMissing { get; init; }

    public static int DefaultWorkers() => Math.Clamp(Environment.ProcessorCount * 2, 4, 24);
}
