using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal static class QueueScreen
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private const int PollsPerCheck = 10;

    public static async Task<bool> WaitForTurnAsync(
        QueueLease lease,
        string folder,
        string build,
        CancellationToken cancellation)
    {
        var status = lease.Check();
        if (status.IsMyTurn) return true;

        var live = ConsoleEx.SupportsColor && ConsoleEx.IsInteractive;
        var started = DateTime.UtcNow;
        var rendered = 0;

        Output.Blank();
        if (!live) Output.Info("Another window is downloading, this one waits for its turn.");

        using var selection = ConsoleInput.SuspendSelection();
        ConsoleEx.HideCursor();
        ConsoleEx.DiscardInput();

        try
        {
            while (true)
            {
                if (live) rendered = ConsoleEx.Repaint(rendered, Frame(status, folder, build, DateTime.UtcNow - started));

                for (var poll = 0; poll < PollsPerCheck; poll++)
                {
                    if (ConsoleEx.TryReadInput(out var input) && input is { IsKey: true, Key.Key: ConsoleKey.Escape })
                    {
                        ConsoleEx.Erase(rendered);
                        return false;
                    }

                    await Task.Delay(PollInterval, cancellation).ConfigureAwait(false);
                }

                status = lease.Check();
                if (!status.IsMyTurn) continue;

                ConsoleEx.Erase(rendered);
                Output.Success($"Your turn, waited {Format.Duration(DateTime.UtcNow - started)}.");
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            ConsoleEx.Erase(rendered);
            return false;
        }
        finally
        {
            ConsoleEx.ShowCursor();
        }
    }

    private static List<string> Frame(QueueStatus status, string folder, string build, TimeSpan waited)
    {
        var running = status.Running;
        var ahead = status.Position;
        var text = ConsoleEx.Width - Output.Indent.Length - 2;
        var bar = Math.Clamp(ConsoleEx.Width - 52, 10, 40);

        var lines = new List<string>
        {
            Output.Indent + Theme.Emphasis("▌", Theme.Accent) + " " + Theme.Emphasis("Queued", Theme.Text) + "  " +
            Theme.Paint(Format.Truncate(build, text - 10), Theme.Cyan),
            string.Empty,
            Output.Indent + Theme.Paint(Format.Truncate("Another window is downloading, this one waits for its turn.", text),
                Theme.Dim),
            string.Empty,
            Row("Downloading now", running is null ? "a build in another window" : running.Build, Theme.Cyan)
        };

        if (running is { StartedUtc: not null })
        {
            var eta = running.EtaSeconds > 1
                ? $"  {Format.Duration(TimeSpan.FromSeconds(running.EtaSeconds))} left"
                : string.Empty;

            lines.Add(Row("Progress", string.Empty, Theme.Text) + Output.ProgressBar(running.Fraction, bar) + "  " +
                      Theme.Paint(Format.Percent(running.Fraction) + eta, Theme.Text));
        }
        else if (running is not null)
        {
            lines.Add(Row("Progress", "getting ready", Theme.Muted));
        }

        lines.Add(Row("Your place", ahead == 1 ? "next in line" : $"{ahead - 1} more waiting ahead of you",
            Theme.Text));
        lines.Add(Row("Waiting for", Format.Duration(waited), Theme.Text));

        if (running?.IsFor(folder) == true)
        {
            lines.Add(string.Empty);
            lines.Add(Output.Indent + Theme.Paint("That window writes into this same folder,", Theme.Warning));
            lines.Add(Output.Indent + Theme.Paint("so afterwards only what is still missing comes down.", Theme.Warning));
        }

        lines.Add(string.Empty);
        lines.Add(Output.Indent + Theme.Paint("esc leaves the queue", Theme.Muted));
        return lines;
    }

    private static string Row(string key, string value, Color color) =>
        Output.Indent + Theme.Paint("│", Theme.Line) + "  " + Theme.Paint(key.PadRight(17), Theme.Muted) +
        Theme.Paint(Format.Truncate(value, Math.Max(10, ConsoleEx.Width - 26)), color);
}
