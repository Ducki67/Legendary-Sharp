using System.Text;
using Legendary_Sharp.Downloader;

namespace Legendary_Sharp.Interface;

internal sealed class ProgressDisplay : IDisposable
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(90);

    private readonly Lock _gate = new();
    private readonly string _action;
    private readonly string _target;
    private readonly bool _live;

    private DateTime _lastPaint = DateTime.MinValue;
    private int _renderedLines;
    private bool _finished;

    public ProgressDisplay(string action, string target)
    {
        _action = action;
        _target = target;
        _live = ConsoleEx.SupportsColor && ConsoleEx.IsInteractive;
        if (_live) ConsoleEx.HideCursor();
    }

    public void Update(in DownloadProgress progress, bool force = false)
    {
        lock (_gate)
        {
            if (_finished) return;

            var now = DateTime.UtcNow;
            if (!force && now - _lastPaint < MinimumInterval) return;
            _lastPaint = now;

            if (!_live)
            {
                WritePlain(progress);
                return;
            }

            Paint(BuildFrame(progress));
        }
    }

    public void Log(string line)
    {
        lock (_gate)
        {
            Erase();
            ConsoleEx.WriteLine(line);
            _lastPaint = DateTime.MinValue;
        }
    }

    public void Finish()
    {
        lock (_gate)
        {
            if (_finished) return;
            _finished = true;
            Erase();
            ConsoleEx.ShowCursor();
        }
    }

    public void Dispose() => Finish();

    private void Erase()
    {
        if (!_live || _renderedLines == 0) return;
        ConsoleEx.Erase(_renderedLines);
        _renderedLines = 0;
    }

    private void Paint(List<string> lines) => _renderedLines = ConsoleEx.Repaint(_renderedLines, lines);

    private List<string> BuildFrame(in DownloadProgress p)
    {
        var width = ConsoleEx.Width;
        var barWidth = Math.Clamp(width - 22, 20, 64);
        var fraction = p.TotalDownloadBytes > 0 ? (double)p.DownloadedBytes / p.TotalDownloadBytes : 0d;

        var lines = new List<string>
        {
            string.Empty,
            Output.Indent + Theme.Emphasis("▌", Theme.Accent) + " " + Theme.Emphasis(_action, Theme.Text) + "  " +
            Theme.Paint(_target, Theme.Cyan),
            string.Empty,
            Output.Indent + Output.ProgressBar(fraction, barWidth) + "  " +
            Theme.Emphasis(Format.Percent(fraction).PadLeft(6), Theme.Text),
            string.Empty,
            Row("Downloaded", $"{Format.Bytes(p.DownloadedBytes)} / {Format.Bytes(p.TotalDownloadBytes)}",
                "Network", Format.Rate(p.BytesPerSecond)),
            Row("Written", $"{Format.Bytes(p.WrittenBytes)} / {Format.Bytes(p.TotalWriteBytes)}",
                "Disk", Format.Rate(p.WriteBytesPerSecond)),
            Row("Files", $"{Format.Count(p.FilesDone)} / {Format.Count(p.FilesTotal)}",
                "Elapsed", Format.Duration(p.Elapsed)),
            Row("Chunks", $"{Format.Count(p.ChunksDone)} / {Format.Count(p.ChunksTotal)}",
                "Remaining", Format.Duration(p.Eta)),
            Row("Cache", $"{Format.Count(p.CachedChunks)} chunks",
                "Workers", $"{p.ActiveWorkers} active"),
            string.Empty,
            Output.Indent + Theme.Paint("→ ", Theme.Muted) +
            Theme.Paint(Format.TruncateStart(p.CurrentFile, Math.Max(20, width - 8)), Theme.Dim),
            string.Empty,
            Output.Indent + Theme.Paint("ctrl+c stops, progress is saved", Theme.Muted)
        };

        return lines;
    }

    private static string Row(string leftKey, string leftValue, string rightKey, string rightValue)
    {
        const int leftKeyWidth = 13;
        const int rightKeyWidth = 11;
        const int edgeWidth = 5;

        var budget = ConsoleEx.Width - 1 - edgeWidth - leftKeyWidth - rightKeyWidth - rightValue.Length;
        var left = Format.Truncate(leftValue, Math.Max(4, budget - 1));
        var column = Math.Max(34, (ConsoleEx.Width - 8) / 2);
        var gap = Math.Clamp(column - leftKeyWidth - left.Length, 1, Math.Max(1, budget - left.Length));

        return Output.Indent + Theme.Paint("│", Theme.Line) + "  "
               + Theme.Paint(leftKey.PadRight(leftKeyWidth), Theme.Muted)
               + Theme.Paint(left, Theme.Text)
               + new string(' ', gap)
               + Theme.Paint(rightKey.PadRight(rightKeyWidth), Theme.Muted)
               + Theme.Paint(rightValue, Theme.Text);
    }

    private void WritePlain(in DownloadProgress p)
    {
        var fraction = p.TotalDownloadBytes > 0 ? (double)p.DownloadedBytes / p.TotalDownloadBytes : 0d;
        ConsoleEx.WriteLine(
            $"  {Format.Percent(fraction),6}  {Format.Bytes(p.DownloadedBytes)} / {Format.Bytes(p.TotalDownloadBytes)}" +
            $"  {Format.Rate(p.BytesPerSecond)}  files {p.FilesDone}/{p.FilesTotal}  eta {Format.Duration(p.Eta)}");
    }
}
