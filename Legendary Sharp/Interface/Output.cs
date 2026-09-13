using System.Text;

namespace Legendary_Sharp.Interface;

internal static class Output
{
    public const string Indent = "  ";

    public static void Banner(string title, string subtitle)
    {
        ConsoleEx.WriteLine();
        ConsoleEx.WriteLine(Indent + Theme.Emphasis("▌", Theme.BrandEnd) + " " +
                            Theme.Emphasis(title, Theme.Text) +
                            (subtitle.Length > 0 ? "  " + Theme.Paint(subtitle, Theme.Muted) : string.Empty));
        ConsoleEx.WriteLine();
    }

    public static void Section(string title)
    {
        ConsoleEx.WriteLine();
        ConsoleEx.WriteLine(Indent + Theme.Emphasis("▌", Theme.BrandEnd) + " " + Theme.Emphasis(title, Theme.Text));
        ConsoleEx.WriteLine();
    }

    public static void Blank() => ConsoleEx.WriteLine();

    public static void Line(string text = "") => ConsoleEx.WriteLine(text.Length == 0 ? text : Indent + text);

    public static void Info(string text) => Status("\u2022", Theme.Cyan, text);

    public static void Success(string text) => Status("\u2713", Theme.Success, text);

    public static void Warn(string text) => Status("!", Theme.Warning, text);

    public static void Error(string text) => Status("\u2717", Theme.Danger, text);

    public static void Step(string text) => Status("\u2192", Theme.Accent, text);

    public static void Detail(string text) => ConsoleEx.WriteLine(Indent + "  " + Theme.Paint(text, Theme.Muted));

    public static void Pair(string key, string value, int keyWidth = 18)
    {
        var padded = key.PadRight(keyWidth);
        ConsoleEx.WriteLine(Indent + Theme.Paint(padded, Theme.Dim) + Theme.Paint(value, Theme.Text));
    }

    public static void Bullet(string text) =>
        ConsoleEx.WriteLine(Indent + Theme.Paint("  \u00b7 ", Theme.Muted) + Theme.Paint(text, Theme.Text));

    public static void Hint(string text) =>
        ConsoleEx.WriteLine(Indent + Theme.Paint(text, Theme.Muted));

    private static void Status(string glyph, Color color, string text) =>
        ConsoleEx.WriteLine(Indent + Theme.Emphasis(glyph, color) + " " + Theme.Paint(text, Theme.Text));

    private static readonly char[] PartialBlocks = ['\u258f', '\u258e', '\u258d', '\u258c', '\u258b', '\u258a', '\u2589'];

    public static string ProgressBar(double fraction, int width)
    {
        fraction = Math.Clamp(fraction, 0d, 1d);
        var exact = fraction * width;
        var filled = (int)exact;
        var remainder = exact - filled;

        if (!ConsoleEx.SupportsColor)
            return new string('#', filled) + new string('.', Math.Max(0, width - filled));

        var builder = new StringBuilder(width * 12);

        for (var column = 0; column < filled; column++)
        {
            builder.Append(Ansi.Foreground(Shade(column, width)));
            builder.Append('\u2588');
        }

        var used = filled;

        if (filled < width && remainder > 0.12d)
        {
            builder.Append(Ansi.Foreground(Shade(filled, width)));
            builder.Append(PartialBlocks[Math.Clamp((int)(remainder * PartialBlocks.Length), 0, PartialBlocks.Length - 1)]);
            used++;
        }

        builder.Append(Ansi.Reset);
        builder.Append(Ansi.Foreground(Theme.Line));
        builder.Append(new string('\u2501', Math.Max(0, width - used)));
        builder.Append(Ansi.Reset);
        return builder.ToString();
    }

    private static Color Shade(int column, int width) =>
        Theme.Brand(width <= 1 ? 0d : (double)column / (width - 1));

    public static string GradientRule(int width, double strength = 0.4d)
    {
        if (width <= 0) return string.Empty;
        if (!ConsoleEx.SupportsColor) return new string('─', width);

        var builder = new StringBuilder(width * 12);
        var ground = Color.FromHex(0x11131C);

        for (var column = 0; column < width; column++)
        {
            builder.Append(Ansi.Foreground(ground.Blend(Shade(column, width), strength)));
            builder.Append('─');
        }

        builder.Append(Ansi.Reset);
        return builder.ToString();
    }
}
