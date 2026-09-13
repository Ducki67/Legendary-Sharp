using System.Text;

namespace Legendary_Sharp.Interface;

internal readonly record struct MarkerInfo(string Glyph, Color Color)
{
    public static readonly MarkerInfo None = new(string.Empty, Theme.Muted);
}

internal abstract class ListView<T>
{
    private int _renderedLines;

    protected ListView(string title, IReadOnlyList<Choice<T>> choices, string hint)
    {
        Title = title;
        Choices = choices;
        Hint = hint;
        Filtered = [.. Enumerable.Range(0, choices.Count)];
    }

    protected string Title { get; }

    protected IReadOnlyList<Choice<T>> Choices { get; }

    protected string Hint { get; }

    protected List<int> Filtered { get; }

    protected int Cursor { get; private set; }

    protected int Offset { get; private set; }

    protected string Query { get; private set; } = string.Empty;

    protected int? CurrentChoice => Filtered.Count == 0 ? null : Filtered[Cursor];

    protected virtual string? Status => null;

    private static int MaxVisible => Math.Clamp(ConsoleEx.Height - 10, 4, 16);

    protected abstract MarkerInfo Marker(int choiceIndex);

    protected abstract bool HandleKey(ConsoleKeyInfo key, out bool accepted);

    protected bool Run()
    {
        ConsoleEx.HideCursor();
        try
        {
            while (true)
            {
                Render();
                var key = ConsoleEx.ReadKey();

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        Move(-1);
                        continue;
                    case ConsoleKey.DownArrow:
                        Move(1);
                        continue;
                    case ConsoleKey.PageUp:
                        Move(-MaxVisible);
                        continue;
                    case ConsoleKey.PageDown:
                        Move(MaxVisible);
                        continue;
                    case ConsoleKey.Home:
                        Move(-Filtered.Count);
                        continue;
                    case ConsoleKey.End:
                        Move(Filtered.Count);
                        continue;
                    case ConsoleKey.Escape:
                        Clear();
                        return false;
                    case ConsoleKey.Backspace when Query.Length > 0:
                        Query = Query[..^1];
                        ApplyFilter();
                        continue;
                }

                if (HandleKey(key, out var accepted))
                {
                    if (!accepted) continue;
                    Clear();
                    return true;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    Query += key.KeyChar;
                    ApplyFilter();
                }
            }
        }
        finally
        {
            ConsoleEx.ShowCursor();
        }
    }

    private void Move(int delta)
    {
        if (Filtered.Count == 0) return;
        Cursor = Math.Clamp(Cursor + delta, 0, Filtered.Count - 1);
        var visible = Math.Min(MaxVisible, Filtered.Count);
        if (Cursor < Offset) Offset = Cursor;
        else if (Cursor >= Offset + visible) Offset = Cursor - visible + 1;
    }

    private void ApplyFilter()
    {
        Filtered.Clear();
        var needle = Query.ToLowerInvariant();

        for (var i = 0; i < Choices.Count; i++)
            if (needle.Length == 0 || Choices[i].SearchKey.Contains(needle, StringComparison.Ordinal))
                Filtered.Add(i);

        Cursor = 0;
        Offset = 0;
    }

    private void Render() => _renderedLines = ConsoleEx.Repaint(_renderedLines, BuildFrame());

    private void Clear()
    {
        ConsoleEx.Erase(_renderedLines);
        _renderedLines = 0;
    }

    private List<string> BuildFrame()
    {
        var content = Math.Max(24, ConsoleEx.Width - Output.Indent.Length - 3);
        var lines = new List<string> { Heading(content), string.Empty };

        if (Filtered.Count == 0)
        {
            lines.Add(Output.Indent + Theme.Paint("  nothing matches \"" + Query + "\"", Theme.Muted));
        }
        else
        {
            var visible = Math.Min(MaxVisible, Filtered.Count);
            Offset = Math.Clamp(Offset, 0, Filtered.Count - visible);
            Cursor = Math.Clamp(Cursor, 0, Filtered.Count - 1);

            var labelWidth = Math.Min(
                Filtered.Select(index => Choices[index].Label.Length).DefaultIfEmpty(0).Max(),
                Math.Max(14, content / 2));

            for (var row = 0; row < visible; row++)
                lines.Add(Entry(Offset + row, labelWidth, content, visible));
        }

        lines.Add(string.Empty);
        if (Status is { } status) lines.Add(Output.Indent + Theme.Paint(status, Theme.Cyan));
        lines.Add(Output.Indent + Theme.Paint(Hint, Theme.Muted));
        return lines;
    }

    private string Heading(int content)
    {
        var line = new StyledLine().Add(Title, Theme.Text, bold: true);

        if (Query.Length > 0)
            line.Add("  filter: " + Format.Truncate(Query, 30), Theme.Cyan);

        if (Filtered.Count > 0 && Filtered.Count != Choices.Count)
            line.Add($"  ({Filtered.Count} of {Choices.Count})", Theme.Muted);

        return Output.Indent + line.Build();
    }

    private string Entry(int index, int labelWidth, int content, int visible)
    {
        var choiceIndex = Filtered[index];
        var choice = Choices[choiceIndex];
        var active = index == Cursor;
        var marker = Marker(choiceIndex);

        var line = new StyledLine().Add(active ? " ❯ " : "   ", Theme.Accent, bold: true);
        if (marker.Glyph.Length > 0) line.Add(marker.Glyph + " ", marker.Color);

        var label = Format.Truncate(choice.Label, labelWidth);
        var pad = choice.Description is null ? label : label.PadRight(labelWidth + 3);
        line.Add(pad, active ? Theme.Text : choice.Enabled ? Theme.Dim : Theme.Muted, active);

        if (choice.Description is { } description)
            line.Add(Format.Truncate(description, Math.Max(0, content - line.Length - 1)),
                active ? Theme.Dim : Theme.Muted);

        line.Pad(content);
        return Output.Indent + line.Build(active ? Theme.Highlight : null) + Scrollbar(index, visible);
    }

    private string Scrollbar(int index, int visible)
    {
        if (Filtered.Count <= visible) return string.Empty;

        var row = index - Offset;
        var thumbSize = Math.Max(1, visible * visible / Filtered.Count);
        var thumbTop = Offset * (visible - thumbSize) / Math.Max(1, Filtered.Count - visible);
        var onThumb = row >= thumbTop && row < thumbTop + thumbSize;

        return Theme.Paint(onThumb ? "┃" : "│", onThumb ? Theme.Accent : Theme.Line);
    }
}
