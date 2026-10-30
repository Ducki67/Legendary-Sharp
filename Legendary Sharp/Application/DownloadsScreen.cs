using System.Diagnostics;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed class DownloadsScreen(Session session)
{
    private static readonly TimeSpan Refresh = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan InputPoll = TimeSpan.FromMilliseconds(40);

    private enum JobAction
    {
        Pause,
        Resume,
        CarryOn,
        Retry,
        Up,
        Down,
        Result,
        Uefn,
        Open,
        Cancel,
        Remove,
        Back
    }

    private Guid? _selected;
    private int _rendered;
    private int _frameTop;
    private int _listTop;
    private int _offset;
    private int _room;
    private int _rows = 12;
    private string _last = string.Empty;

    public async Task RunAsync(Guid? focus, CancellationToken cancellation)
    {
        _selected = focus;

        while (true)
        {
            InteractiveShell.Header();
            _rendered = 0;
            _last = string.Empty;

            var chosen = Watch(cancellation);
            if (chosen is null) return;

            await ActAsync(chosen, cancellation).ConfigureAwait(false);
        }
    }

    private JobView? Watch(CancellationToken cancellation)
    {
        using var selection = ConsoleInput.SuspendSelection();
        ConsoleEx.HideCursor();

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var jobs = session.Downloads.Snapshot();
                var index = Math.Max(0, jobs.ToList().FindIndex(job => job.Id == _selected));
                if (jobs.Count > 0) _selected = jobs[Math.Min(index, jobs.Count - 1)].Id;

                Render(jobs);

                var deadline = DateTime.UtcNow + Refresh;
                while (DateTime.UtcNow < deadline && !cancellation.IsCancellationRequested)
                {
                    if (!ConsoleEx.TryReadInput(out var input))
                    {
                        Thread.Sleep(InputPoll);
                        continue;
                    }

                    var outcome = Handle(input, jobs);
                    if (outcome == Outcome.Leave) return Erase(null);
                    if (outcome == Outcome.Open) return Erase(jobs.FirstOrDefault(job => job.Id == _selected));
                    break;
                }
            }

            return Erase(null);
        }
        finally
        {
            ConsoleEx.ShowCursor();
        }
    }

    private enum Outcome
    {
        Stay,
        Open,
        Leave
    }

    private Outcome Handle(InputEvent input, IReadOnlyList<JobView> jobs)
    {
        var index = jobs.ToList().FindIndex(job => job.Id == _selected);

        if (!input.IsKey)
        {
            switch (input.Pointer)
            {
                case PointerAction.ScrollUp:
                    Select(jobs, index - input.Steps);
                    return Outcome.Stay;
                case PointerAction.ScrollDown:
                    Select(jobs, index + input.Steps);
                    return Outcome.Stay;
                case PointerAction.Click:
                    var row = input.Row - _frameTop - _listTop;
                    if (row < 0 || row >= Math.Min(_rows, jobs.Count - _offset)) return Outcome.Stay;
                    _selected = jobs[_offset + row].Id;
                    return Outcome.Open;
                default:
                    return Outcome.Stay;
            }
        }

        switch (input.Key.Key)
        {
            case ConsoleKey.Escape:
                return Outcome.Leave;
            case ConsoleKey.UpArrow:
                Select(jobs, index - 1);
                return Outcome.Stay;
            case ConsoleKey.DownArrow:
                Select(jobs, index + 1);
                return Outcome.Stay;
            case ConsoleKey.Home:
                Select(jobs, 0);
                return Outcome.Stay;
            case ConsoleKey.End:
                Select(jobs, jobs.Count - 1);
                return Outcome.Stay;
            case ConsoleKey.Tab:
                Select(jobs, index == jobs.Count - 1 ? 0 : jobs.Count - 1);
                return Outcome.Stay;
            case ConsoleKey.Enter when index >= 0:
                return Outcome.Open;
            case ConsoleKey.Spacebar when index >= 0:
                Toggle(jobs[index]);
                return Outcome.Stay;
            case ConsoleKey.C:
                session.Downloads.ClearFinished();
                return Outcome.Stay;
            default:
                return Outcome.Stay;
        }
    }

    private void Select(IReadOnlyList<JobView> jobs, int index)
    {
        if (jobs.Count == 0) return;
        _selected = jobs[Math.Clamp(index, 0, jobs.Count - 1)].Id;
    }

    private void Toggle(JobView job)
    {
        switch (job.State)
        {
            case JobState.Running or JobState.Waiting or JobState.Queued:
                session.Downloads.Pause(job.Id);
                break;
            case JobState.Paused:
                session.Downloads.Resume(job.Id);
                break;
            case JobState.NeedsAttention:
                session.Downloads.CarryOn(job.Id);
                break;
        }
    }

    private JobView? Erase(JobView? result)
    {
        ConsoleEx.Erase(_rendered);
        _rendered = 0;
        _last = string.Empty;
        return result;
    }

    private void Render(IReadOnlyList<JobView> jobs)
    {
        _room = ConsoleEx.RowsBelowCursor() + _rendered;
        var lines = Frame(jobs);
        var signature = string.Join('\n', lines);
        if (_rendered > 0 && signature == _last) return;

        _last = signature;
        _rendered = ConsoleEx.Repaint(_rendered, lines);
        _frameTop = ConsoleEx.CursorRow() - _rendered;
    }

    private List<string> Frame(IReadOnlyList<JobView> jobs)
    {
        var lines = new List<string> { Title(jobs), string.Empty };

        if (jobs.Count == 0)
        {
            _listTop = lines.Count;
            lines.Add(Output.Indent + Theme.Paint("  Nothing in your downloads yet.", Theme.Muted));
            lines.Add(string.Empty);
            lines.Add(Output.Indent + Theme.Paint("Pick Download a build to add one.   esc back", Theme.Muted));
            return lines;
        }

        var selected = jobs.FirstOrDefault(job => job.Id == _selected) ?? jobs[0];
        lines.AddRange(Detail(selected));
        lines.Add(string.Empty);

        _listTop = lines.Count;
        var width = Math.Min(Math.Max(16, jobs.Max(job => job.Title.Length)), Math.Max(16, ConsoleEx.Width / 2 - 10));

        var position = Math.Max(0, jobs.ToList().FindIndex(job => job.Id == selected.Id));
        _rows = Math.Clamp(_room - 1 - lines.Count - 3, 3, 12);
        var rows = Math.Min(_rows, jobs.Count);
        _offset = Math.Clamp(_offset, Math.Max(0, position - rows + 1), position);
        _offset = Math.Clamp(_offset, 0, jobs.Count - rows);

        for (var index = _offset; index < _offset + rows; index++)
            lines.Add(Row(jobs[index], jobs[index].Id == selected.Id, width));

        if (jobs.Count > rows)
            lines.Add(Output.Indent + Theme.Paint($"   {_offset + 1} to {_offset + rows} of {jobs.Count}", Theme.Muted));

        lines.Add(string.Empty);
        lines.Add(Output.Indent + Theme.Paint(Format.Truncate(
            $"{Hints.Move}   ⏎ actions   space pause or resume   c clear finished   esc back",
            ConsoleEx.Width - Output.Indent.Length - 1), Theme.Muted));
        return lines;
    }

    private static string Title(IReadOnlyList<JobView> jobs)
    {
        var line = Output.Indent + Theme.Emphasis("▌", Theme.Accent) + " " + Theme.Emphasis("Downloads", Theme.Text);

        var queued = jobs.Count(job => job.State is JobState.Queued or JobState.Waiting);
        var paused = jobs.Count(job => job.State == JobState.Paused);
        var waiting = jobs.Where(job => job.State is JobState.Queued or JobState.Waiting or JobState.Running)
            .Sum(job => Math.Max(0, job.DownloadSize - (job.Progress?.DownloadedBytes ?? 0)));

        var parts = new List<string>();
        if (queued > 0) parts.Add($"{queued} queued");
        if (paused > 0) parts.Add($"{paused} paused");
        if (waiting > 0) parts.Add($"{Format.Bytes(waiting)} left to download");

        return parts.Count == 0 ? line : line + "   " + Theme.Paint(string.Join("  ·  ", parts), Theme.Muted);
    }

    private static List<string> Detail(JobView job)
    {
        var lines = new List<string> { Field("Download", job.Title, Theme.Cyan) };
        var bar = Math.Clamp(ConsoleEx.Width - 40, 12, 48);

        if (job is { State: JobState.Running, Progress: { } p })
        {
            var easing = p.Easing.Length > 0 ? $", eased off because {p.Easing}" : string.Empty;

            lines.Add(Output.Indent + Theme.Paint("│", Theme.Line) + "  " + Output.ProgressBar(p.Fraction, bar) + "  " +
                      Theme.Emphasis(Format.Percent(p.Fraction), Theme.Text) +
                      Theme.Paint($"   {Format.Duration(p.Eta)} left", Theme.Muted));
            lines.Add(Field("Downloaded", $"{Format.Bytes(p.DownloadedBytes)} of {Format.Bytes(p.TotalDownloadBytes)}" +
                                          $"   at {Format.Rate(p.BytesPerSecond)}", Theme.Text));
            lines.Add(Field("Files", $"{Format.Count(p.FilesDone)} of {Format.Count(p.FilesTotal)}" +
                                     $"   disk {Format.Rate(p.WriteBytesPerSecond)}", Theme.Text));
            lines.Add(Field("Workers", $"{p.ActiveWorkers} busy, {p.WorkerLimit} allowed{easing}",
                p.Easing.Length > 0 ? Theme.Warning : Theme.Text));
        }
        else if (job is { State: JobState.Waiting, Blocker: { } blocker })
        {
            var progress = blocker.StartedUtc is null ? "getting ready" : Format.Percent(blocker.Fraction);
            lines.Add(Field("Status", $"waiting for another window, it is downloading {blocker.Build} ({progress})",
                Theme.Muted));
        }
        else
        {
            lines.Add(Field("Status", Describe(job), Colour(job)));
            if (job.Message.Length > 0) lines.Add(Field("", job.Message, Theme.Dim));
            if (job.Uefn is { } uefn) lines.Add(Field("", $"{uefn.Display} is available, open actions to add it", Theme.Cyan));
        }

        lines.Add(Field("Folder", job.Root, Theme.Dim));
        return lines;
    }

    private static string Field(string key, string value, Color color) =>
        Output.Indent + Theme.Paint("│", Theme.Line) + "  " + Theme.Paint(key.PadRight(12), Theme.Muted) +
        Theme.Paint(Format.Truncate(value, Math.Max(10, ConsoleEx.Width - 20)), color);

    private static string Row(JobView job, bool active, int width)
    {
        var (glyph, color) = Glyph(job);
        var line = new StyledLine()
            .Add(active ? " ❯ " : "   ", Theme.Accent, bold: true)
            .Add(glyph + " ", color)
            .Add(Format.Truncate(job.Title, width).PadRight(width + 3), active ? Theme.Text : Theme.Dim, active)
            .Add(Describe(job), Colour(job));

        line.Pad(Math.Max(24, ConsoleEx.Width - Output.Indent.Length - 3));
        return Output.Indent + line.Build(active ? Theme.Highlight : null);
    }

    private static (string Glyph, Color Color) Glyph(JobView job) => job.State switch
    {
        JobState.Running => ("●", Theme.Accent),
        JobState.Waiting => ("◌", Theme.Muted),
        JobState.Queued => ("○", Theme.Dim),
        JobState.Paused => ("‖", Theme.Warning),
        JobState.NeedsAttention => ("!", Theme.Warning),
        JobState.Done when job.IsComplete => ("✓", Theme.Success),
        JobState.Done => ("!", Theme.Warning),
        JobState.Failed => ("✗", Theme.Danger),
        _ => ("–", Theme.Muted)
    };

    private static Color Colour(JobView job) => job.State switch
    {
        JobState.Running => Theme.Cyan,
        JobState.NeedsAttention or JobState.Paused => Theme.Warning,
        JobState.Done when job.IsComplete => Theme.Success,
        JobState.Done => Theme.Warning,
        JobState.Failed => Theme.Danger,
        _ => Theme.Muted
    };

    private static string Describe(JobView job) => job.State switch
    {
        JobState.Running => $"downloading  {Format.Percent(job.Fraction)}",
        JobState.Waiting => "waiting for another window",
        JobState.Queued => $"queued  {Format.Bytes(job.DownloadSize)}",
        JobState.Paused => job.Progress is null ? "paused" : $"paused at {Format.Percent(job.Fraction)}",
        JobState.NeedsAttention => "needs you, part of it is missing",
        JobState.Done when job.Result is { Failures.Count: > 0 } result =>
            $"done, {Format.Count(result.Failures.Count)} files missing",
        JobState.Done => "done",
        JobState.Failed => "failed",
        _ => "cancelled"
    };

    private async Task ActAsync(JobView job, CancellationToken cancellation)
    {
        var actions = Actions(job);

        if (!SelectionList<JobAction>.TryPick(job.Title, actions, out var action)) return;

        switch (action)
        {
            case JobAction.Pause:
                session.Downloads.Pause(job.Id);
                break;
            case JobAction.Resume or JobAction.Retry:
                session.Downloads.Resume(job.Id);
                break;
            case JobAction.CarryOn:
                session.Downloads.CarryOn(job.Id);
                break;
            case JobAction.Up:
                session.Downloads.Shift(job.Id, -1);
                break;
            case JobAction.Down:
                session.Downloads.Shift(job.Id, 1);
                break;
            case JobAction.Remove:
                session.Downloads.Remove(job.Id);
                break;
            case JobAction.Cancel:
                Output.Blank();
                if (Prompt.Ask($"Stop {job.Title} and take it off the list? What is downloaded stays in the folder.", false) == true)
                    session.Downloads.Cancel(job.Id);
                break;
            case JobAction.Open:
                OpenFolder(job.Root);
                break;
            case JobAction.Result:
                ShowResult(job);
                break;
            case JobAction.Uefn when job.Uefn is { } release:
                var queued = await UefnWorkflow.InstallAsync(session, job.Root, release, cancellation).ConfigureAwait(false);
                if (queued is not null) _selected = queued.Id;
                else ConsoleEx.PauseForKey("Press any key to go back.");
                break;
        }
    }

    private static List<Choice<JobAction>> Actions(JobView job)
    {
        var actions = new List<Choice<JobAction>>();

        switch (job.State)
        {
            case JobState.Running or JobState.Waiting:
                actions.Add(new(JobAction.Pause, "Pause", "stop for now, it keeps its place and carries on later"));
                break;
            case JobState.Queued:
                actions.Add(new(JobAction.Up, "Move up", "download it sooner"));
                actions.Add(new(JobAction.Down, "Move down", "download it later"));
                actions.Add(new(JobAction.Pause, "Pause", "skip it until you resume it"));
                break;
            case JobState.Paused:
                actions.Add(new(JobAction.Resume, "Resume", "put it back in the queue"));
                actions.Add(new(JobAction.Up, "Move up", "resume it sooner"));
                actions.Add(new(JobAction.Down, "Move down", "resume it later"));
                break;
            case JobState.NeedsAttention:
                actions.Add(new(JobAction.CarryOn, "Carry on without the missing files",
                    "download everything Epic still hosts and list the rest"));
                break;
            case JobState.Failed:
                actions.Add(new(JobAction.Retry, "Try again", "carries on from where it stopped"));
                break;
            case JobState.Cancelled:
                actions.Add(new(JobAction.Retry, "Start again", "carries on from what is already in the folder"));
                break;
            case JobState.Done:
                actions.Add(new(JobAction.Result, "Show the result", "files written, speed and anything missing"));
                if (job.Uefn is { } uefn) actions.Add(new(JobAction.Uefn, "Install UEFN", uefn.Display));
                break;
        }

        actions.Add(new(JobAction.Open, "Open the folder", job.Root));

        if (job.State is JobState.Done or JobState.Failed or JobState.Cancelled)
            actions.Add(new(JobAction.Remove, "Remove from the list", "the files stay where they are"));
        else
            actions.Add(new(JobAction.Cancel, "Cancel", "stop and take it off the list, the files stay"));

        actions.Add(new(JobAction.Back, "Back", string.Empty));
        return actions;
    }

    private static void ShowResult(JobView job)
    {
        if (job.Result is { } result) DownloadWorkflow.Report(result, job.Root);
        else
        {
            Output.Blank();
            Output.Success(job.Message.Length > 0 ? job.Message : "Done.");
        }

        ConsoleEx.PauseForKey("Press any key to go back.");
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Output.Blank();
            Output.Pair("Folder", folder);
            ConsoleEx.PauseForKey("Press any key to go back.");
        }
    }
}
