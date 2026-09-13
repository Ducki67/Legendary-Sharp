namespace Legendary_Sharp.Downloader;

internal sealed class RateMeter(TimeSpan window)
{
    private readonly Queue<Sample> _samples = new();
    private readonly Lock _gate = new();

    public void Record(long total, DateTime timestamp)
    {
        lock (_gate)
        {
            _samples.Enqueue(new Sample(timestamp, total));
            var cutoff = timestamp - window;
            while (_samples.Count > 2 && _samples.Peek().Timestamp < cutoff) _samples.Dequeue();
        }
    }

    public double PerSecond
    {
        get
        {
            lock (_gate)
            {
                if (_samples.Count < 2) return 0d;
                var first = _samples.Peek();
                var last = _samples.Last();
                var seconds = (last.Timestamp - first.Timestamp).TotalSeconds;
                return seconds <= 0.05d ? 0d : (last.Total - first.Total) / seconds;
            }
        }
    }

    private readonly record struct Sample(DateTime Timestamp, long Total);
}
