namespace Legendary_Sharp.Interface;

internal sealed class MultiSelectList<T> : ListView<T>
{
    private readonly HashSet<int> _checked = [];
    private readonly Func<IReadOnlyList<T>, string>? _summary;

    private MultiSelectList(
        string title,
        IReadOnlyList<Choice<T>> choices,
        IEnumerable<int> preselected,
        Func<IReadOnlyList<T>, string>? summary,
        string hint)
        : base(title, choices, hint)
    {
        _summary = summary;
        foreach (var index in preselected) _checked.Add(index);
    }

    public static bool TryPick(
        string title,
        IReadOnlyList<Choice<T>> choices,
        IEnumerable<int> preselected,
        out List<T> values,
        Func<IReadOnlyList<T>, string>? summary = null,
        string? hint = null)
    {
        values = [];
        if (choices.Count == 0) return false;

        var view = new MultiSelectList<T>(title, choices, preselected, summary,
            hint ?? $"{Hints.Move}   {Hints.Toggle} toggles   ctrl+a all   ctrl+n none   ⏎ confirm   esc back");

        if (!view.Run()) return false;
        values = view.Selected;
        return true;
    }

    private List<T> Selected => [.. _checked.Order().Select(index => Choices[index].Value)];

    protected override string? Status
    {
        get
        {
            var selected = Selected;
            var prefix = $"{selected.Count} of {Choices.Count} selected";
            return _summary is null ? prefix : prefix + "   ·   " + _summary(selected);
        }
    }

    protected override ConsoleKey ClickKey => ConsoleKey.Spacebar;

    protected override MarkerInfo Marker(int choiceIndex) =>
        _checked.Contains(choiceIndex)
            ? new MarkerInfo("◉", Theme.Success)
            : new MarkerInfo("○", Theme.Muted);

    protected override bool HandleKey(ConsoleKeyInfo key, out bool accepted)
    {
        accepted = false;

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                accepted = true;
                return true;
            case ConsoleKey.Spacebar:
                var index = CurrentChoice;
                if (index is not null && !_checked.Add(index.Value)) _checked.Remove(index.Value);
                return true;
        }

        if (!key.Modifiers.HasFlag(ConsoleModifiers.Control)) return false;

        switch (key.Key)
        {
            case ConsoleKey.A:
                foreach (var i in Filtered) _checked.Add(i);
                return true;
            case ConsoleKey.N:
                foreach (var i in Filtered) _checked.Remove(i);
                return true;
            default:
                return false;
        }
    }
}
