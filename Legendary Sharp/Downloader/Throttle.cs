namespace Legendary_Sharp.Downloader;

internal sealed class Throttle(int limit)
{
    private readonly Lock _gate = new();
    private readonly Queue<TaskCompletionSource> _waiters = new();

    private int _limit = Math.Max(1, limit);
    private int _active;
    private string _reason = string.Empty;

    public int Maximum { get; } = Math.Max(1, limit);

    public int Limit
    {
        get
        {
            lock (_gate) return _limit;
        }
    }

    public string Reason
    {
        get
        {
            lock (_gate) return _reason;
        }
    }

    public void Adjust(int limit, string reason)
    {
        lock (_gate)
        {
            _limit = Math.Clamp(limit, 1, Maximum);
            _reason = _limit < Maximum ? reason : string.Empty;
            GrantWaiting();
        }
    }

    public async Task WaitAsync(CancellationToken cancellation)
    {
        TaskCompletionSource waiter;

        lock (_gate)
        {
            if (_active < _limit && _waiters.Count == 0)
            {
                _active++;
                return;
            }

            waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
        }

        await using (cancellation.Register(() => waiter.TrySetCanceled(cancellation)).ConfigureAwait(false))
        {
            await waiter.Task.ConfigureAwait(false);
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            _active--;
            GrantWaiting();
        }
    }

    private void GrantWaiting()
    {
        while (_active < _limit && _waiters.TryDequeue(out var waiter))
            if (waiter.TrySetResult())
                _active++;
    }
}
