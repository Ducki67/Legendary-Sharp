namespace Legendary_Sharp.Application;

internal static class Interrupt
{
    private static CancellationTokenSource _current = new();

    public static CancellationToken Begin()
    {
        var next = new CancellationTokenSource();
        Interlocked.Exchange(ref _current, next);
        return next.Token;
    }

    public static void Signal()
    {
        try
        {
            Volatile.Read(ref _current).Cancel();
        }
        catch (AggregateException)
        {
        }
    }
}
