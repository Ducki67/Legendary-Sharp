namespace Legendary_Sharp.Interface;

internal static class Ansi
{
    public const string Escape = "\u001b[";
    public const string Reset = "\u001b[0m";
    public const string Bold = "\u001b[1m";
    public const string BoldOff = "\u001b[22m";
    public const string Dim = "\u001b[2m";
    public const string Italic = "\u001b[3m";
    public const string Underline = "\u001b[4m";
    public const string HideCursor = "\u001b[?25l";
    public const string ShowCursor = "\u001b[?25h";
    public const string EraseToLineEnd = "\u001b[K";
    public const string Home = "\u001b[H";
    public const string ClearScreen = "\u001b[2J\u001b[H";
    public const string ClearBelow = "\u001b[0J";
    public const string LineStart = "\r";

    public static string Foreground(Color color) => $"\u001b[38;2;{color.R};{color.G};{color.B}m";

    public static string Background(Color color) => $"\u001b[48;2;{color.R};{color.G};{color.B}m";

    public static string Up(int lines) => lines <= 0 ? string.Empty : $"\u001b[{lines}A";

    public static string Down(int lines) => lines <= 0 ? string.Empty : $"\u001b[{lines}B";

    public static string Column(int column) => $"\u001b[{column}G";
}

internal readonly record struct Color(byte R, byte G, byte B)
{
    public static Color FromHex(uint value) => new((byte)(value >> 16), (byte)(value >> 8), (byte)value);

    public Color Blend(Color other, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return new Color(
            (byte)(R + (other.R - R) * amount),
            (byte)(G + (other.G - G) * amount),
            (byte)(B + (other.B - B) * amount));
    }
}
