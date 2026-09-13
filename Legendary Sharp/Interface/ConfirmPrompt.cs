using System.Text;

namespace Legendary_Sharp.Interface;

internal sealed class ConfirmPrompt
{
    private readonly string _question;
    private bool _value;
    private int _renderedLines;

    private ConfirmPrompt(string question, bool initial)
    {
        _question = question;
        _value = initial;
    }

    public static bool? Ask(string question, bool defaultValue = true)
    {
        if (!ConsoleEx.SupportsColor || !ConsoleEx.IsInteractive) return Fallback(question, defaultValue);
        return new ConfirmPrompt(question, defaultValue).Run();
    }

    private bool? Run()
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
                    case ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Tab:
                        _value = !_value;
                        continue;
                    case ConsoleKey.Enter:
                        Clear();
                        return _value;
                    case ConsoleKey.Escape:
                        Clear();
                        return null;
                }

                switch (char.ToLowerInvariant(key.KeyChar))
                {
                    case 'y':
                        Clear();
                        return true;
                    case 'n':
                        Clear();
                        return false;
                }
            }
        }
        finally
        {
            ConsoleEx.ShowCursor();
        }
    }

    private void Render()
    {
        var line = new StyledLine()
            .Add("? ", Theme.Accent, bold: true)
            .Add(_question, Theme.Text, bold: true)
            .Add("   ", Theme.Text);

        var lines = new List<string>
        {
            Output.Indent + line.Build() + Option("Yes", _value) + "  " + Option("No", !_value),
            Output.Indent + Theme.Paint("←→ switch   y / n   ⏎ confirm   esc cancel", Theme.Muted)
        };

        _renderedLines = ConsoleEx.Repaint(_renderedLines, lines);
    }

    private static string Option(string label, bool active) =>
        active
            ? new StyledLine().Add(" " + label + " ", Theme.Text, bold: true).Build(Theme.Highlight)
            : new StyledLine().Add(" " + label + " ", Theme.Muted).Build();

    private void Clear()
    {
        ConsoleEx.Erase(_renderedLines);
        _renderedLines = 0;
    }

    private static bool Fallback(string question, bool defaultValue)
    {
        var hint = defaultValue ? "Y/n" : "y/N";

        while (true)
        {
            ConsoleEx.Write(Output.Indent + "? " + question + $" [{hint}] > ");
            var input = Console.ReadLine()?.Trim() ?? string.Empty;
            if (input.Length == 0) return defaultValue;
            if (input.StartsWith('y') || input.StartsWith('Y')) return true;
            if (input.StartsWith('n') || input.StartsWith('N')) return false;
        }
    }
}
