using System.Text;

namespace Legendary_Sharp.Interface;

internal static class Logo
{
    private static readonly LogoArt Wide = new(94,
    [
        "██      ███████  ██████  ███████ ███    ██ ██████   █████  ██████  ██    ██     ██████  ██ ██ ",
        "██      ██      ██       ██      ████   ██ ██   ██ ██   ██ ██   ██  ██  ██     ██      ███████",
        "██      █████   ██   ███ █████   ██ ██  ██ ██   ██ ███████ ██████    ████      ██       ██ ██ ",
        "██      ██      ██    ██ ██      ██  ██ ██ ██   ██ ██   ██ ██   ██    ██       ██      ███████",
        "███████ ███████  ██████  ███████ ██   ████ ██████  ██   ██ ██   ██    ██        ██████  ██ ██ "
    ], "v" + Application.AppInfo.Version, 79);

    private static readonly LogoArt Narrow = new(75,
    [
        "██      ███████  ██████  ███████ ███    ██ ██████   █████  ██████  ██    ██",
        "██      ██      ██       ██      ████   ██ ██   ██ ██   ██ ██   ██  ██  ██ ",
        "██      █████   ██   ███ █████   ██ ██  ██ ██   ██ ███████ ██████    ████  ",
        "██      ██      ██    ██ ██      ██  ██ ██ ██   ██ ██   ██ ██   ██    ██   ",
        "███████ ███████  ██████  ███████ ██   ████ ██████  ██   ██ ██   ██    ██   "
    ], "C #   ·   v" + Application.AppInfo.Version, -1);

    public static void Render(string subtitle) => ConsoleEx.WriteBlock(Lines(subtitle));

    public static List<string> Lines(string subtitle)
    {
        var art = Pick();
        if (art is null) return Compact(subtitle);

        var lines = new List<string> { string.Empty };

        foreach (var row in art.Rows)
            lines.Add(Output.Indent + (ConsoleEx.SupportsColor ? Gradient(row, art) : row));

        lines.Add(Output.Indent + Underline(art));
        lines.Add(string.Empty);
        lines.Add(Output.Indent + Theme.Paint(subtitle, Theme.Dim));
        lines.Add(Output.Indent + Credit());
        return lines;
    }

    private static LogoArt? Pick()
    {
        var available = ConsoleEx.Width - Output.Indent.Length - 1;
        if (available >= Wide.Width) return Wide;
        return available >= Narrow.Width ? Narrow : null;
    }

    private static List<string> Compact(string subtitle) =>
    [
        string.Empty,
        Output.Indent +
        Theme.Emphasis("LEGENDARY", Theme.BrandStart) + " " +
        Theme.Emphasis("C#", Theme.CSharp) + "  " +
        Theme.Paint("v" + Application.AppInfo.Version, Theme.Muted),
        Output.Indent + Theme.Paint(subtitle, Theme.Dim),
        Output.Indent + Credit()
    ];

    private static string Credit()
    {
        var credit = Theme.Paint(Application.AppInfo.Credit, Theme.Muted);
        return Application.AppInfo.Repository.Length == 0
            ? credit
            : credit + Theme.Paint("   ·   " + Application.AppInfo.Repository, Theme.Muted);
    }

    private static string Underline(LogoArt art)
    {
        var rule = Math.Max(4, art.Width - art.Badge.Length - 3);
        if (!ConsoleEx.SupportsColor) return new string('─', rule) + "   " + art.Badge;

        var builder = new StringBuilder();

        for (var column = 0; column < rule; column++)
        {
            builder.Append(Ansi.Foreground(Fade(Theme.Brand((double)column / Math.Max(1, rule - 1)), 0.45d)));
            builder.Append('━');
        }

        builder.Append(Ansi.Reset);
        builder.Append("   ");
        builder.Append(Theme.Paint(art.Badge, Theme.CSharp));
        return builder.ToString();
    }

    private static string Gradient(string line, LogoArt art)
    {
        var builder = new StringBuilder(line.Length * 12);
        var lastColor = default(Color);
        var painting = false;

        foreach (var (character, index) in line.Select((value, index) => (value, index)))
        {
            if (character == ' ')
            {
                if (painting)
                {
                    builder.Append(Ansi.Reset);
                    painting = false;
                }

                builder.Append(' ');
                continue;
            }

            var color = art.SolidFrom >= 0 && index >= art.SolidFrom
                ? Theme.CSharp
                : Theme.Brand((double)index / Math.Max(1, (art.SolidFrom > 0 ? art.SolidFrom : line.Length) - 1));

            if (!painting || color != lastColor)
            {
                builder.Append(Ansi.Foreground(color));
                lastColor = color;
                painting = true;
            }

            builder.Append(character);
        }

        if (painting) builder.Append(Ansi.Reset);
        return builder.ToString();
    }

    private static Color Fade(Color color, double amount) => Color.FromHex(0x11131C).Blend(color, amount);

    private sealed record LogoArt(int Width, string[] Rows, string Badge, int SolidFrom);
}
