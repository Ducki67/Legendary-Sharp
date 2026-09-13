namespace Legendary_Sharp.Interface;

internal sealed class Panel
{
    private readonly List<Entry> _entries = [];

    public Panel Row(string key, string value, Color? color = null)
    {
        _entries.Add(new Entry(key, value, color, false));
        return this;
    }

    public Panel Gap()
    {
        _entries.Add(new Entry(string.Empty, string.Empty, null, true));
        return this;
    }

    public void Render()
    {
        if (_entries.Count == 0) return;

        var keyWidth = _entries.Where(entry => !entry.IsGap).Select(entry => entry.Key.Length).DefaultIfEmpty(0).Max();
        var valueWidth = Math.Max(10, ConsoleEx.Width - Output.Indent.Length - keyWidth - 7);

        foreach (var entry in _entries)
        {
            if (entry.IsGap)
            {
                ConsoleEx.WriteLine(Output.Indent + Theme.Paint("│", Theme.Line));
                continue;
            }

            ConsoleEx.WriteLine(Output.Indent + Theme.Paint("│", Theme.Line) + "  "
                                + Theme.Paint(entry.Key.PadRight(keyWidth + 3), Theme.Muted)
                                + Theme.Paint(Format.Truncate(entry.Value, valueWidth), entry.Color ?? Theme.Text));
        }
    }

    private readonly record struct Entry(string Key, string Value, Color? Color, bool IsGap);
}
