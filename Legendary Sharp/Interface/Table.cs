using System.Text;

namespace Legendary_Sharp.Interface;

internal sealed class Table
{
    private readonly List<Column> _columns = [];
    private readonly List<string[]> _rows = [];

    public Table Add(string header, Alignment alignment = Alignment.Left, int maxWidth = 0, Color? color = null)
    {
        _columns.Add(new Column(header, alignment, maxWidth, color));
        return this;
    }

    public Table Row(params string[] cells)
    {
        _rows.Add(cells);
        return this;
    }

    public int RowCount => _rows.Count;

    public void Render()
    {
        if (_columns.Count == 0) return;

        var widths = new int[_columns.Count];
        for (var i = 0; i < _columns.Count; i++) widths[i] = _columns[i].Header.Length;

        foreach (var row in _rows)
            for (var i = 0; i < _columns.Count && i < row.Length; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);

        for (var i = 0; i < _columns.Count; i++)
            if (_columns[i].MaxWidth > 0)
                widths[i] = Math.Min(widths[i], _columns[i].MaxWidth);

        Fit(widths);

        var header = new StringBuilder(Output.Indent);
        for (var i = 0; i < _columns.Count; i++)
        {
            if (i > 0) header.Append("  ");
            header.Append(Theme.Paint(Pad(_columns[i].Header, widths[i], _columns[i].Alignment), Theme.Muted));
        }

        ConsoleEx.WriteLine(header.ToString());
        ConsoleEx.WriteLine(Output.Indent + Output.GradientRule(widths.Sum() + (_columns.Count - 1) * 2));

        foreach (var row in _rows)
        {
            var line = new StringBuilder(Output.Indent);
            for (var i = 0; i < _columns.Count; i++)
            {
                if (i > 0) line.Append("  ");
                var value = i < row.Length ? row[i] : string.Empty;
                line.Append(Theme.Paint(Pad(value, widths[i], _columns[i].Alignment), _columns[i].Color ?? Theme.Text));
            }

            ConsoleEx.WriteLine(line.ToString());
        }
    }

    private void Fit(int[] widths)
    {
        var available = ConsoleEx.Width - Output.Indent.Length - (_columns.Count - 1) * 2 - 1;
        var total = widths.Sum();
        if (total <= available) return;

        var order = Enumerable.Range(0, widths.Length).OrderByDescending(i => widths[i]).ToArray();
        foreach (var index in order)
        {
            if (total <= available) break;
            var floor = Math.Min(widths[index], Math.Max(6, _columns[index].Header.Length));
            var reducible = widths[index] - floor;
            if (reducible <= 0) continue;
            var cut = Math.Min(reducible, total - available);
            widths[index] -= cut;
            total -= cut;
        }
    }

    private static string Pad(string value, int width, Alignment alignment)
    {
        var text = Format.Truncate(value, width);
        return alignment switch
        {
            Alignment.Right => text.PadLeft(width),
            Alignment.Center => text.PadLeft((width + text.Length) / 2).PadRight(width),
            _ => text.PadRight(width)
        };
    }

    private readonly record struct Column(string Header, Alignment Alignment, int MaxWidth, Color? Color);

    public enum Alignment
    {
        Left,
        Right,
        Center
    }
}
