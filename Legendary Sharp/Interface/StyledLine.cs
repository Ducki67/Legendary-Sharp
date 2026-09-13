using System.Text;

namespace Legendary_Sharp.Interface;

internal sealed class StyledLine
{
    private readonly List<Run> _runs = [];

    public int Length { get; private set; }

    public StyledLine Add(string text, Color color, bool bold = false)
    {
        if (text.Length == 0) return this;
        _runs.Add(new Run(text, color, bold));
        Length += text.Length;
        return this;
    }

    public StyledLine Pad(int width)
    {
        var missing = width - Length;
        return missing > 0 ? Add(new string(' ', missing), Theme.Text) : this;
    }

    public string Build(Color? background = null)
    {
        var builder = new StringBuilder();

        if (!ConsoleEx.SupportsColor)
        {
            foreach (var run in _runs) builder.Append(run.Text);
            return builder.ToString();
        }

        if (background is { } fill) builder.Append(Ansi.Background(fill));

        foreach (var run in _runs)
        {
            if (run.Bold) builder.Append(Ansi.Bold);
            builder.Append(Ansi.Foreground(run.Color));
            builder.Append(run.Text);
            if (run.Bold) builder.Append(Ansi.BoldOff);
        }

        builder.Append(Ansi.Reset);
        return builder.ToString();
    }

    private readonly record struct Run(string Text, Color Color, bool Bold);
}
