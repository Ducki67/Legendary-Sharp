using System.Runtime.InteropServices;

namespace Legendary_Sharp.Interface;

internal static partial class Clipboard
{
    private const uint UnicodeText = 13;
    private const int OpenAttempts = 5;

    public static string? ReadText()
    {
        if (!OperatingSystem.IsWindows() || !TryOpen()) return null;

        try
        {
            var handle = GetClipboardData(UnicodeText);
            if (handle == nint.Zero) return null;

            var pointer = GlobalLock(handle);
            if (pointer == nint.Zero) return null;

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool TryOpen()
    {
        for (var attempt = 1; attempt <= OpenAttempts; attempt++)
        {
            if (OpenClipboard(nint.Zero)) return true;
            Thread.Sleep(20);
        }

        return false;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);
}
