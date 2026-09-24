using System.Runtime.InteropServices;

namespace Legendary_Sharp.Interface;

internal enum PointerMode
{
    Off,
    Scroll,
    Full
}

internal enum PointerAction
{
    None,
    ScrollUp,
    ScrollDown,
    Click
}

internal readonly record struct InputEvent(ConsoleKeyInfo Key, PointerAction Pointer, int Row, int Steps = 1)
{
    public bool IsKey => Pointer == PointerAction.None;
}

internal static partial class ConsoleInput
{
    private const int StdInputHandle = -10;

    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEdit = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;

    private const ushort KeyEventType = 0x0001;
    private const ushort MouseEventType = 0x0002;

    private const uint MouseWheeled = 0x0004;
    private const uint LeftButton = 0x0001;
    private const int WheelNotch = 120;

    private static nint _handle;
    private static uint _originalMode;
    private static bool _enabled;
    private static int _wheel;
    private static PointerMode _mode = PointerMode.Full;

    public static bool ScrollAvailable => _enabled && _mode != PointerMode.Off;

    public static bool ClickAvailable => _enabled && _mode == PointerMode.Full;

    public static bool PointerAvailable => ScrollAvailable;

    public static void Apply(PointerMode mode)
    {
        _mode = mode;
        if (mode == PointerMode.Off) Restore();
        else Enable();
    }

    private static void Enable()
    {
        if (!OperatingSystem.IsWindows() || _enabled) return;

        _handle = GetStdHandle(StdInputHandle);
        if (_handle == nint.Zero || _handle == -1) return;
        if (!GetConsoleMode(_handle, out _originalMode)) return;

        var mode = (_originalMode | EnableExtendedFlags | EnableMouseInput | EnableProcessedInput) & ~EnableQuickEdit;
        _enabled = SetConsoleMode(_handle, mode);
    }

    public static void Restore()
    {
        if (!_enabled) return;
        SetConsoleMode(_handle, _originalMode);
        _enabled = false;
    }

    public static InputEvent Read()
    {
        if (!_enabled) return new InputEvent(Console.ReadKey(intercept: true), PointerAction.None, 0);

        while (true)
        {
            if (!ReadConsoleInput(_handle, out var record, 1, out var read) || read == 0)
                return new InputEvent(Console.ReadKey(intercept: true), PointerAction.None, 0);

            if (Translate(record, out var input)) return input;
        }
    }

    public static IDisposable SuspendSelection()
    {
        if (!OperatingSystem.IsWindows()) return SelectionScope.None;

        var handle = GetStdHandle(StdInputHandle);
        if (handle == nint.Zero || handle == -1 || !GetConsoleMode(handle, out var mode)) return SelectionScope.None;
        if ((mode & EnableQuickEdit) == 0) return SelectionScope.None;

        return SetConsoleMode(handle, (mode | EnableExtendedFlags) & ~EnableQuickEdit)
            ? new SelectionScope(handle, mode)
            : SelectionScope.None;
    }

    public static bool TryRead(out InputEvent input)
    {
        input = default;

        if (!_enabled)
        {
            if (!KeyWaiting()) return false;
            input = new InputEvent(Console.ReadKey(intercept: true), PointerAction.None, 0);
            return true;
        }

        while (GetNumberOfConsoleInputEvents(_handle, out var pending) && pending > 0)
        {
            if (!ReadConsoleInput(_handle, out var record, 1, out var read) || read == 0) return false;
            if (Translate(record, out input)) return true;
        }

        return false;
    }

    public static void Discard()
    {
        if (_enabled)
        {
            FlushConsoleInputBuffer(_handle);
            return;
        }

        while (KeyWaiting()) Console.ReadKey(intercept: true);
    }

    private static bool KeyWaiting()
    {
        try
        {
            return Console.KeyAvailable;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool Translate(InputRecord record, out InputEvent input)
    {
        input = default;

        switch (record.EventType)
        {
            case KeyEventType when record.Key.KeyDown != 0 && !IsModifier(record.Key.VirtualKeyCode):
                input = new InputEvent(ToKeyInfo(record.Key), PointerAction.None, 0);
                return true;

            case MouseEventType when _mode != PointerMode.Off &&
                                     record.Mouse.EventFlags == MouseWheeled:
                var delta = (short)(record.Mouse.ButtonState >> 16);
                if (delta == 0) return false;
                if (Math.Sign(delta) != Math.Sign(_wheel)) _wheel = 0;

                _wheel += delta;
                var notches = _wheel / WheelNotch;
                if (notches == 0) return false;

                _wheel -= notches * WheelNotch;
                input = new InputEvent(default,
                    notches > 0 ? PointerAction.ScrollUp : PointerAction.ScrollDown,
                    record.Mouse.Position.Y,
                    Math.Abs(notches));
                return true;

            case MouseEventType when _mode == PointerMode.Full &&
                                     record.Mouse.EventFlags == 0 &&
                                     (record.Mouse.ButtonState & LeftButton) != 0:
                input = new InputEvent(default, PointerAction.Click, record.Mouse.Position.Y);
                return true;

            default:
                return false;
        }
    }

    private static bool IsModifier(ushort virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or 0x5D or 0x90 or 0x91
            or >= 0xA0 and <= 0xA5;

    private static ConsoleKeyInfo ToKeyInfo(KeyEventRecord key)
    {
        var state = key.ControlKeyState;
        var shift = (state & 0x0010) != 0;
        var alt = (state & 0x0003) != 0;
        var control = (state & 0x000C) != 0;

        return new ConsoleKeyInfo((char)key.UnicodeChar, (ConsoleKey)key.VirtualKeyCode, shift, alt, control);
    }

    private sealed class SelectionScope(nint handle, uint mode) : IDisposable
    {
        public static readonly IDisposable None = new SelectionScope(nint.Zero, 0);

        private int _released;

        public void Dispose()
        {
            if (handle == nint.Zero || Interlocked.Exchange(ref _released, 1) != 0) return;
            SetConsoleMode(handle, mode);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyEventRecord
    {
        public int KeyDown;
        public ushort RepeatCount;
        public ushort VirtualKeyCode;
        public ushort VirtualScanCode;
        public ushort UnicodeChar;
        public uint ControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseEventRecord
    {
        public Coord Position;
        public uint ButtonState;
        public uint ControlKeyState;
        public uint EventFlags;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KeyEventRecord Key;
        [FieldOffset(4)] public MouseEventRecord Mouse;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint handle, uint mode);

    [LibraryImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadConsoleInput(nint handle, out InputRecord record, uint length, out uint read);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNumberOfConsoleInputEvents(nint handle, out uint count);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushConsoleInputBuffer(nint handle);
}
