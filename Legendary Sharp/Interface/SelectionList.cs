namespace Legendary_Sharp.Interface;

internal sealed class SelectionList<T> : ListView<T>
{
    private readonly string? _status;
    private readonly Func<IReadOnlyList<string>>? _banner;
    private readonly Func<ConsoleKeyInfo, Choice<T>?>? _shortcut;

    private Choice<T>? _result;

    private SelectionList(string title, IReadOnlyList<Choice<T>> choices, string hint, string? status,
        Func<IReadOnlyList<string>>? banner, Func<ConsoleKeyInfo, Choice<T>?>? shortcut)
        : base(title, choices, hint)
    {
        _status = status;
        _banner = banner;
        _shortcut = shortcut;
    }

    protected override string? Status => _status;

    protected override Func<IReadOnlyList<string>>? Banner => _banner;

    public static bool TryPick(
        string title,
        IReadOnlyList<Choice<T>> choices,
        out T value,
        string? hint = null,
        string? status = null,
        Func<IReadOnlyList<string>>? banner = null,
        Func<ConsoleKeyInfo, Choice<T>?>? shortcut = null)
    {
        value = default!;
        if (choices.Count == 0) return false;

        var view = new SelectionList<T>(title, choices,
            hint ?? $"{Hints.Move}   {Hints.Jump}   {Hints.Choose}   type to filter   esc back", status, banner, shortcut);

        if (!view.Run() || view._result is null) return false;
        value = view._result.Value;
        return true;
    }

    protected override MarkerInfo Marker(int choiceIndex) => MarkerInfo.None;

    protected override bool HandleKey(ConsoleKeyInfo key, out bool accepted)
    {
        accepted = false;

        if (_shortcut?.Invoke(key) is { } picked)
        {
            _result = picked;
            accepted = true;
            return true;
        }

        if (key.Key != ConsoleKey.Enter) return false;

        var index = CurrentChoice;
        if (index is null || !Choices[index.Value].Enabled) return true;

        _result = Choices[index.Value];
        accepted = true;
        return true;
    }
}
