using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Legendary_Sharp.Downloader;

internal static partial class FileLocks
{
    private const int ErrorMoreData = 234;
    private const int ProcessInfoSize = 668;
    private const int SessionKeyBytes = 66;

    public static bool IsLockViolation(Exception error) =>
        error is IOException && (error.HResult & 0xFFFF) is 32 or 33;

    public static IOException InUse(string path, string displayName, Exception inner)
    {
        var holders = Holders(path);

        var message = holders.Count > 0
            ? $"{displayName} is in use by {string.Join(", ", holders)}. Close it and start again."
            : $"{displayName} is in use by another program. Close the game, any other Legendary Sharp window " +
              "or anything else that may have it open, then start again.";

        return new IOException(message, inner);
    }

    public static IReadOnlyList<string> Holders(string path)
    {
        if (!OperatingSystem.IsWindows()) return [];

        var key = Marshal.AllocHGlobal(SessionKeyBytes);
        var name = Marshal.StringToHGlobalUni(path);
        var buffer = nint.Zero;
        uint session = 0;
        var started = false;

        try
        {
            if (RmStartSession(out session, 0, key) != 0) return [];
            started = true;

            nint[] files = [name];
            if (RmRegisterResources(session, 1, files, 0, nint.Zero, 0, nint.Zero) != 0) return [];

            uint capacity = 4;

            while (true)
            {
                buffer = Marshal.AllocHGlobal((int)capacity * ProcessInfoSize);
                var count = capacity;
                var result = RmGetList(session, out var needed, ref count, buffer, out _);

                if (result == ErrorMoreData && needed > capacity)
                {
                    Marshal.FreeHGlobal(buffer);
                    buffer = nint.Zero;
                    capacity = needed + 2;
                    continue;
                }

                if (result != 0) return [];

                var holders = new List<string>((int)count);
                for (var index = 0; index < count; index++)
                    holders.Add(Describe(Marshal.ReadInt32(buffer + index * ProcessInfoSize)));

                return holders;
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return [];
        }
        finally
        {
            if (buffer != nint.Zero) Marshal.FreeHGlobal(buffer);
            if (started) RmEndSession(session);
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(key);
        }
    }

    private static string Describe(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return $"{process.ProcessName}.exe (process {processId})";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return $"process {processId}";
        }
    }

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmStartSession(out uint session, uint flags, nint sessionKey);

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmRegisterResources(
        uint session,
        uint fileCount,
        nint[] files,
        uint applicationCount,
        nint applications,
        uint serviceCount,
        nint services);

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmGetList(
        uint session,
        out uint needed,
        ref uint count,
        nint processes,
        out uint rebootReasons);

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmEndSession(uint session);
}
