namespace Legendary_Sharp.Interface;

internal static class Taskbar
{
    private const int Hidden = 0;
    private const int Normal = 1;
    private const int Failed = 2;
    private const int Busy = 3;
    private const int Halted = 4;

    private static readonly bool Supported = Environment.GetEnvironmentVariable("WT_SESSION") is not null;

    private static int _last = -1;

    public static void Progress(double fraction) => Set(Normal, fraction);

    public static void Waiting() => Set(Busy, 0d);

    public static void Paused(double fraction) => Set(Halted, fraction);

    public static void Error(double fraction) => Set(Failed, fraction);

    public static void Clear() => Set(Hidden, 0d);

    private static void Set(int state, double fraction)
    {
        if (!Supported || !ConsoleEx.SupportsColor || !ConsoleEx.IsInteractive) return;

        var percent = (int)Math.Clamp(Math.Round(fraction * 100d), 0, 100);
        var key = state * 1000 + percent;
        if (Interlocked.Exchange(ref _last, key) == key) return;

        ConsoleEx.Write($"\u001b]9;4;{state};{percent}\u001b\\");
    }
}
