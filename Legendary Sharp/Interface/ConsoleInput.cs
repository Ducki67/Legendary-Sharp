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

internal readonly record struct InputEvent(ConsoleKeyInfo Key, PointerAction Pointer, int Row)
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

    private static nint _handle;
    private static uint _originalMode;
    private static bool _enabled;
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

            switch (record.EventType)
            {
                case KeyEventType when record.Key.KeyDown != 0:
                    return new InputEvent(ToKeyInfo(record.Key), PointerAction.None, 0);

                case MouseEventType when _mode != PointerMode.Off &&
                                         record.Mouse.EventFlags == MouseWheeled:
                    var delta = (short)(record.Mouse.ButtonState >> 16);
                    if (delta == 0) continue;
                    return new InputEvent(default,
                        delta > 0 ? PointerAction.ScrollUp : PointerAction.ScrollDown,
                        record.Mouse.Position.Y);

                case MouseEventType when _mode == PointerMode.Full &&
                                         record.Mouse.EventFlags == 0 &&
                                         (record.Mouse.ButtonState & LeftButton) != 0:
                    return new InputEvent(default, PointerAction.Click, record.Mouse.Position.Y);
            }
        }
    }

    private static ConsoleKeyInfo ToKeyInfo(KeyEventRecord key)
    {
        var state = key.ControlKeyState;
        var shift = (state & 0x0010) != 0;
        var alt = (state & 0x0003) != 0;
        var control = (state & 0x000C) != 0;

        return new ConsoleKeyInfo((char)key.UnicodeChar, (ConsoleKey)key.VirtualKeyCode, shift, alt, control);
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
}
