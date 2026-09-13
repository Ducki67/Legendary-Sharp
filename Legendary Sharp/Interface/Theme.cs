namespace Legendary_Sharp.Interface;

internal static class Theme
{
    public static readonly Color CSharp = Color.FromHex(0x7355DD);

    public static readonly Color BrandStart = Color.FromHex(0x7FD8FF);
    public static readonly Color BrandMiddle = Color.FromHex(0x7BA6F2);
    public static readonly Color BrandEnd = CSharp;

    public static readonly Color Accent = Color.FromHex(0x9B84F0);
    public static readonly Color AccentSoft = Color.FromHex(0x5B4FC4);
    public static readonly Color Cyan = Color.FromHex(0x7FD8FF);
    public static readonly Color Text = Color.FromHex(0xE6E8F0);
    public static readonly Color Dim = Color.FromHex(0x9AA0B4);
    public static readonly Color Muted = Color.FromHex(0x5C6274);
    public static readonly Color Line = Color.FromHex(0x333A4D);
    public static readonly Color Success = Color.FromHex(0x4ADE80);
    public static readonly Color Warning = Color.FromHex(0xFBBF24);
    public static readonly Color Danger = Color.FromHex(0xF87171);
    public static readonly Color Highlight = Color.FromHex(0x201B38);

    public static Color Brand(double position)
    {
        position = Math.Clamp(position, 0d, 1d);

        return position < 0.5d
            ? BrandStart.Blend(BrandMiddle, position * 2d)
            : BrandMiddle.Blend(BrandEnd, (position - 0.5d) * 2d);
    }

    public static string Paint(string text, Color color) =>
        ConsoleEx.SupportsColor ? $"{Ansi.Foreground(color)}{text}{Ansi.Reset}" : text;

    public static string Emphasis(string text, Color color) =>
        ConsoleEx.SupportsColor ? $"{Ansi.Bold}{Ansi.Foreground(color)}{text}{Ansi.Reset}" : text;
}
