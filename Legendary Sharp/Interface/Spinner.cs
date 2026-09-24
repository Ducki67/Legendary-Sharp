namespace Legendary_Sharp.Interface;

internal sealed class Spinner : IDisposable
{
    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _loop;
    private readonly string _message;
    private readonly bool _live;
    private readonly IDisposable? _selection;

    private Spinner(string message)
    {
        _message = message;
        _live = ConsoleEx.SupportsColor && ConsoleEx.IsInteractive;

        if (!_live)
        {
            Output.Step(message);
            _loop = Task.CompletedTask;
            return;
        }

        _selection = ConsoleInput.SuspendSelection();
        ConsoleEx.HideCursor();
        _loop = Task.Run(Animate);
    }

    public static Spinner Start(string message) => new(message);

    public static async Task<T> Run<T>(string message, Func<Task<T>> work)
    {
        using var spinner = Start(message);
        return await work().ConfigureAwait(false);
    }

    public void Succeed(string message)
    {
        Stop();
        Output.Success(message);
    }

    public void Fail(string message)
    {
        Stop();
        Output.Error(message);
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        if (_cancellation.IsCancellationRequested) return;
        _cancellation.Cancel();

        if (!_live) return;

        try
        {
            _loop.Wait(TimeSpan.FromMilliseconds(300));
        }
        catch (AggregateException)
        {
        }

        ConsoleEx.Write(Ansi.LineStart + Ansi.EraseToLineEnd);
        ConsoleEx.ShowCursor();
        _selection?.Dispose();
    }

    private async Task Animate()
    {
        var frame = 0;
        while (!_cancellation.IsCancellationRequested)
        {
            var glyph = Theme.Paint(Frames[frame++ % Frames.Length], Theme.Accent);
            ConsoleEx.Write(Ansi.LineStart + Output.Indent + glyph + " " +
                            Theme.Paint(_message, Theme.Dim) + Ansi.EraseToLineEnd);

            try
            {
                await Task.Delay(80, _cancellation.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }
}
