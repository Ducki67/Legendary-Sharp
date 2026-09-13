using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal readonly record struct AvailabilityReport(int Sampled, int Available)
{
    public int Missing => Sampled - Available;

    public double Fraction => Sampled == 0 ? 1d : (double)Available / Sampled;

    public bool IsComplete => Missing == 0;

    public bool IsHopeless => Sampled > 0 && Available == 0;
}

internal static class AvailabilityProbe
{
    public static async Task<AvailabilityReport> RunAsync(
        DownloadPlan plan,
        ChunkSource source,
        int sampleSize,
        CancellationToken cancellation)
    {
        var sample = Sample(plan, sampleSize);
        if (sample.Count == 0) return new AvailabilityReport(0, 0);

        var available = 0;
        using var limiter = new SemaphoreSlim(8, 8);

        var probes = sample.Select(async chunk =>
        {
            await limiter.WaitAsync(cancellation).ConfigureAwait(false);

            try
            {
                if (await source.ExistsAsync(chunk, plan.Manifest.FeatureLevel, cancellation).ConfigureAwait(false))
                    Interlocked.Increment(ref available);
            }
            finally
            {
                limiter.Release();
            }
        });

        await Task.WhenAll(probes).ConfigureAwait(false);
        return new AvailabilityReport(sample.Count, available);
    }

    private static List<ChunkInfo> Sample(DownloadPlan plan, int sampleSize)
    {
        var total = plan.ChunkOrder.Count;
        if (total == 0) return [];

        var count = Math.Min(sampleSize, total);
        var stride = (double)total / count;
        var sample = new List<ChunkInfo>(count);

        for (var i = 0; i < count; i++)
        {
            var index = Math.Min(total - 1, (int)(i * stride));
            sample.Add(plan.Manifest.Chunk(plan.ChunkOrder[index]));
        }

        return sample;
    }
}
