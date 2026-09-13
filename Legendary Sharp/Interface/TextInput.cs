using System.Text;

namespace Legendary_Sharp.Interface;

internal sealed class TextInput
{
    private readonly string _label;
    private readonly string? _fallback;
    private readonly Func<string, string?>? _validate;
    private readonly bool _completePaths;
    private readonly string _hint;

    private readonly StringBuilder _value = new();
    private string[] _candidates = [];
    private int _candidate = -1;
    private int _caret;
    private int _renderedLines;
    private string? _error;

    private TextInput(string label, string? initial, string? fallback, Func<string, string?>? validate,
        bool completePaths, string hint)
    {
        _label = label;
        _fallback = fallback;
        _validate = validate;
        _completePaths = completePaths;
        _hint = hint;

        if (initial is not null)
        {
            _value.Append(initial);
            _caret = initial.Length;
        }
    }

    public static string? Ask(
        string label,
        string? initial = null,
        Func<string, string?>? validate = null,
        bool completePaths = false)
    {
        if (!ConsoleEx.SupportsColor || !ConsoleEx.IsInteractive) return Fallback(label, initial, validate);

        var hint = completePaths
            ? "tab completes   ⏎ confirm   esc cancel"
            : "⏎ confirm   esc cancel";

        return new TextInput(label, initial, initial, validate, completePaths, hint).Run();
    }

    private string? Run()
    {
        ConsoleEx.HideCursor();
        try
        {
            while (true)
            {
                Render();
                var key = ConsoleEx.ReadKey();

                if (key.Key != ConsoleKey.Tab) _candidate = -1;

                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                        Clear();
                        return null;

                    case ConsoleKey.Enter:
                    {
                        var text = Current();
                        _error = text.Length == 0 ? "A value is required." : _validate?.Invoke(text);
                        if (_error is not null) continue;
                        Clear();
                        return text;
                    }

                    case ConsoleKey.Tab when _completePaths:
                        Complete();
                        continue;

                    case ConsoleKey.LeftArrow:
                        _caret = Math.Max(0, _caret - 1);
                        continue;
                    case ConsoleKey.RightArrow:
                        _caret = Math.Min(_value.Length, _caret + 1);
                        continue;
                    case ConsoleKey.Home:
                        _caret = 0;
                        continue;
                    case ConsoleKey.End:
                        _caret = _value.Length;
                        continue;

                    case ConsoleKey.Backspace when _caret > 0:
                        _value.Remove(--_caret, 1);
                        _error = null;
                        continue;
                    case ConsoleKey.Delete when _caret < _value.Length:
                        _value.Remove(_caret, 1);
                        _error = null;
                        continue;
                }

                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.U)
                {
                    _value.Clear();
                    _caret = 0;
                    _error = null;
                    continue;
                }

                if (char.IsControl(key.KeyChar)) continue;

                _value.Insert(_caret++, key.KeyChar);
                _error = null;
            }
        }
        finally
        {
            ConsoleEx.ShowCursor();
        }
    }

    private string Current()
    {
        var text = _value.ToString().Trim();
        return text.Length == 0 && _fallback is not null ? _fallback : text;
    }

    private void Complete()
    {
        var text = _value.ToString();

        if (_candidate < 0)
        {
            _candidates = Candidates(text);
            if (_candidates.Length == 0) return;
            _candidate = 0;
        }
        else
        {
            _candidate = (_candidate + 1) % _candidates.Length;
        }

        _value.Clear();
        _value.Append(_candidates[_candidate]);
        _caret = _value.Length;
        _error = null;
    }

    private static string[] Candidates(string text)
    {
        try
        {
            var cleaned = Prompt.Clean(text);
            var parent = Path.GetDirectoryName(cleaned);
            var prefix = Path.GetFileName(cleaned);

            if (string.IsNullOrEmpty(parent))
            {
                parent = cleaned;
                prefix = string.Empty;
            }

            if (!Directory.Exists(parent)) return [];

            return
            [
                .. Directory.EnumerateDirectories(parent)
                    .Where(path => prefix.Length == 0 ||
                                   Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .Take(50)
            ];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private void Render()
    {
        var width = Math.Max(24, ConsoleEx.Width - Output.Indent.Length - 4);
        var text = _value.ToString();
        var lines = new List<string>
        {
            Output.Indent + new StyledLine()
                .Add("? ", Theme.Accent, bold: true)
                .Add(_label, Theme.Text, bold: true)
                .Build(),
            Output.Indent + Field(text, width)
        };

        if (_error is not null)
            lines.Add(Output.Indent + Theme.Paint("  " + _error, Theme.Danger));
        else if (_candidate >= 0 && _candidates.Length > 1)
            lines.Add(Output.Indent + Theme.Paint($"  match {_candidate + 1} of {_candidates.Length}", Theme.Cyan));
        else if (_fallback is not null && text.Length == 0)
            lines.Add(Output.Indent + Theme.Paint("  empty keeps " + _fallback, Theme.Muted));
        else
            lines.Add(string.Empty);

        lines.Add(Output.Indent + Theme.Paint(_hint, Theme.Muted));

        _renderedLines = ConsoleEx.Repaint(_renderedLines, lines);
    }

    private string Field(string text, int width)
    {
        var visible = Format.TruncateStart(text, width);
        var caret = Math.Clamp(_caret - (text.Length - visible.Length), 0, visible.Length);
        var line = new StyledLine().Add("│ ", Theme.Accent);

        line.Add(visible[..caret], Theme.Text);
        line.Add(caret < visible.Length ? visible[caret].ToString() : " ", Theme.Accent, bold: true);
        if (caret < visible.Length) line.Add(visible[(caret + 1)..], Theme.Text);

        return line.Build();
    }

    private void Clear()
    {
        ConsoleEx.Erase(_renderedLines);
        _renderedLines = 0;
    }

    private static string? Fallback(string label, string? initial, Func<string, string?>? validate)
    {
        while (true)
        {
            var suffix = initial is null ? string.Empty : $" [{initial}]";
            ConsoleEx.Write(Output.Indent + "? " + label + suffix + " > ");

            var input = Console.ReadLine()?.Trim() ?? string.Empty;
            if (input.Length == 0 && initial is not null) input = initial;
            if (input.Length == 0) continue;

            var error = validate?.Invoke(input);
            if (error is null) return input;
            Output.Warn(error);
        }
    }
}
