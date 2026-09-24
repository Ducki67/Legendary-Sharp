using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal readonly record struct AvailabilityReport(int Sampled, int Available, int Unreachable)
{
    public int Checked => Sampled - Unreachable;

    public int Missing => Checked - Available;

    public double Fraction => Checked == 0 ? 1d : (double)Available / Checked;

    public bool IsComplete => Missing == 0;

    public bool IsHopeless => Checked > 0 && Available == 0;

    public bool IsOffline => Sampled > 0 && Checked == 0;
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
        if (sample.Count == 0) return new AvailabilityReport(0, 0, 0);

        var available = 0;
        var unreachable = 0;
        using var limiter = new SemaphoreSlim(8, 8);

        var probes = sample.Select(async chunk =>
        {
            await limiter.WaitAsync(cancellation).ConfigureAwait(false);

            try
            {
                var presence = await source
                    .ProbeAsync(chunk, plan.Manifest.FeatureLevel, cancellation)
                    .ConfigureAwait(false);

                if (presence == ChunkPresence.Present) Interlocked.Increment(ref available);
                else if (presence == ChunkPresence.Unreachable) Interlocked.Increment(ref unreachable);
            }
            finally
            {
                limiter.Release();
            }
        });

        await Task.WhenAll(probes).ConfigureAwait(false);
        return new AvailabilityReport(sample.Count, available, unreachable);
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
