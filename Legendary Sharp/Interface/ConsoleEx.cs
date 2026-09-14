using System.Runtime.InteropServices;
using System.Text;

namespace Legendary_Sharp.Interface;

internal static partial class ConsoleEx
{
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    private static readonly Lock Gate = new();

    public static bool SupportsColor { get; private set; }

    public static bool IsInteractive { get; private set; }

    public static int Width => IsInteractive ? Math.Clamp(SafeWidth(), 60, 200) : 100;

    public static int Height => IsInteractive ? Math.Clamp(SafeHeight(), 12, 80) : 30;

    public static void Initialise()
    {
        IsInteractive = !Console.IsOutputRedirected && !Console.IsInputRedirected;

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }

        SupportsColor = Environment.GetEnvironmentVariable("NO_COLOR") is null
                        && !Console.IsOutputRedirected
                        && EnableVirtualTerminal();
    }

    public static void Write(string text)
    {
        lock (Gate) Console.Out.Write(text);
    }

    public static void WriteLine(string text = "")
    {
        lock (Gate) Console.Out.Write(text + Environment.NewLine);
    }

    public static void Atomic(Action action)
    {
        lock (Gate) action();
    }

    public static int Repaint(int previous, IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        if (previous > 0) builder.Append(Ansi.Up(previous));

        foreach (var line in lines)
        {
            builder.Append(line);
            builder.Append(Ansi.EraseToLineEnd);
            builder.Append('\n');
        }

        var extra = previous - lines.Count;
        for (var i = 0; i < extra; i++)
        {
            builder.Append(Ansi.EraseToLineEnd);
            builder.Append('\n');
        }

        if (extra > 0) builder.Append(Ansi.Up(extra));

        Write(builder.ToString());
        return lines.Count;
    }

    public static void Erase(int lines)
    {
        if (lines > 0) Write(Ansi.Up(lines) + Ansi.ClearBelow);
    }

    public static void WriteBlock(IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines) builder.Append(line).Append(Environment.NewLine);
        Write(builder.ToString());
    }

    public static void Screen(IReadOnlyList<string> lines)
    {
        if (!SupportsColor)
        {
            Clear();
            WriteBlock(lines);
            return;
        }

        var builder = new StringBuilder(Ansi.Home);

        foreach (var line in lines)
        {
            builder.Append(line);
            builder.Append(Ansi.EraseToLineEnd);
            builder.Append('\n');
        }

        builder.Append(Ansi.ClearBelow);
        Write(builder.ToString());
    }

    public static void HideCursor()
    {
        if (SupportsColor) Write(Ansi.HideCursor);
    }

    public static void ShowCursor()
    {
        if (SupportsColor) Write(Ansi.ShowCursor);
    }

    public static void Clear()
    {
        if (SupportsColor) Write(Ansi.ClearScreen);
        else if (IsInteractive) Console.Clear();
    }

    public static ConsoleKeyInfo ReadKey()
    {
        while (true)
        {
            var input = ConsoleInput.Read();
            if (input.IsKey) return input.Key;
        }
    }

    public static InputEvent ReadInput() => ConsoleInput.Read();

    public static void ApplyPointer(PointerMode mode)
    {
        if (IsInteractive) ConsoleInput.Apply(mode);
    }

    public static int CursorRow()
    {
        try
        {
            return Console.CursorTop;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    public static void PauseForKey(string message)
    {
        if (!IsInteractive) return;
        ConsoleEx.WriteLine();
        ConsoleEx.WriteLine("  " + Theme.Paint(message, Theme.Muted));
        Console.ReadKey(intercept: true);
    }

    private static int SafeWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            return 100;
        }
    }

    private static int SafeHeight()
    {
        try
        {
            return Console.WindowHeight;
        }
        catch (IOException)
        {
            return 30;
        }
    }

    private static bool EnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows()) return true;

        var handle = GetStdHandle(StdOutputHandle);
        if (handle == nint.Zero || handle == -1) return false;
        if (!GetConsoleMode(handle, out var mode)) return false;
        if ((mode & EnableVirtualTerminalProcessing) != 0) return true;
        return SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint handle, uint mode);
}
