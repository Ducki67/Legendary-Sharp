using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Legendary_Sharp.Downloader;

internal sealed partial class LoadMonitor : IDisposable
{
    private const string GameProcess = "FortniteClient-Win64-Shipping";
    private const double BusyProcessor = 0.85;
    private const int FullMemory = 92;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Throttle _throttle;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private ulong _lastBusy;
    private ulong _lastTotal;
    private TimeSpan _lastOwn;

    private LoadMonitor(Throttle throttle)
    {
        _throttle = throttle;
        Sample(out _);
        _loop = Task.Run(RunAsync);
    }

    public static LoadMonitor? Watch(Throttle throttle) => OperatingSystem.IsWindows() ? new LoadMonitor(throttle) : null;

    private async Task RunAsync()
    {
        using var ticker = new PeriodicTimer(Interval);

        try
        {
            while (await ticker.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                Decide();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Decide()
    {
        var maximum = _throttle.Maximum;
        var current = _throttle.Limit;
        var floor = Math.Min(2, maximum);

        if (GameRunning())
        {
            _throttle.Adjust(Math.Max(floor, maximum / 4), "Fortnite is running");
            return;
        }

        if (MemoryLoad() >= FullMemory)
        {
            _throttle.Adjust(floor, "memory is nearly full");
            return;
        }

        if (Sample(out var others) && others >= BusyProcessor)
        {
            _throttle.Adjust(Math.Max(floor, current / 2), "your PC is busy");
            return;
        }

        if (current < maximum)
            _throttle.Adjust(current + Math.Max(1, maximum / 8), "speeding back up");
    }

    private bool Sample(out double others)
    {
        others = 0;
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return false;

        var total = kernel + user;
        var busy = total - idle;
        using var self = Process.GetCurrentProcess();
        var own = self.TotalProcessorTime;

        var totalDelta = total - _lastTotal;
        var busyDelta = busy - _lastBusy;
        var ownDelta = (ulong)Math.Max(0, (own - _lastOwn).Ticks);
        var first = _lastTotal == 0;

        _lastTotal = total;
        _lastBusy = busy;
        _lastOwn = own;

        if (first || totalDelta == 0) return false;

        others = Math.Clamp((double)(busyDelta > ownDelta ? busyDelta - ownDelta : 0) / totalDelta, 0, 1);
        return true;
    }

    private static bool GameRunning()
    {
        var processes = Process.GetProcessesByName(GameProcess);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    private static uint MemoryLoad()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? status.Load : 0;
    }

    public void Dispose()
    {
        _stop.Cancel();

        try
        {
            _loop.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        _stop.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint Load;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
