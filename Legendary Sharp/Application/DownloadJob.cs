using Legendary_Sharp.Downloader;
using Legendary_Sharp.Fortnite;

namespace Legendary_Sharp.Application;

internal enum JobState
{
    Queued,
    Waiting,
    Running,
    Paused,
    NeedsAttention,
    Done,
    Failed,
    Cancelled
}

internal sealed record JobView(
    Guid Id,
    string Title,
    string Action,
    string Root,
    JobState State,
    long DownloadSize,
    DownloadProgress? Progress,
    DownloadResult? Result,
    string Message,
    QueueTicket? Blocker,
    UefnRelease? Uefn,
    DateTime? Finished)
{
    public bool IsActive => State is JobState.Queued or JobState.Waiting or JobState.Running;

    public bool IsFinished => State is JobState.Done or JobState.Failed or JobState.Cancelled;

    public bool IsComplete => State == JobState.Done && Result is not { Failures.Count: > 0 };

    public double Fraction => Progress?.Fraction ?? 0d;
}

internal sealed class DownloadJob(DownloadRequest request, string root, DownloadPlan plan, DownloadOptions options)
{
    private readonly Lock _gate = new();

    private JobState _state = JobState.Queued;
    private DownloadProgress? _progress;
    private DownloadResult? _result;
    private string _message = string.Empty;
    private QueueTicket? _blocker;
    private UefnRelease? _uefn;
    private DateTime? _finished;
    private CancellationTokenSource? _run;
    private bool _cancelRequested;

    public Guid Id { get; } = Guid.NewGuid();

    public DownloadRequest Request { get; } = request;

    public string Root { get; } = root;

    public DownloadPlan Plan { get; set; } = plan;

    public DownloadOptions Options { get; set; } = options;

    public string Title => Request.IsAddon
        ? $"UEFN {Request.Source.Manifest.ShortVersion}"
        : Request.RepairFiles is not null
            ? $"{Request.Source.Manifest.ShortVersion} repair"
            : Request.Source.Manifest.ShortVersion;

    public JobState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    public bool CancelRequested
    {
        get
        {
            lock (_gate) return _cancelRequested;
        }
    }

    public JobView View()
    {
        lock (_gate)
            return new JobView(Id, Title, Request.Action, Root, _state, Plan.DownloadSize, _progress, _result,
                _message, _blocker, _uefn, _finished);
    }

    public void Begin(CancellationTokenSource run)
    {
        lock (_gate)
        {
            _run = run;
            _cancelRequested = false;
            _state = JobState.Waiting;
            _message = string.Empty;
            _progress = null;
            _result = null;
            _finished = null;
        }
    }

    public void End()
    {
        lock (_gate) _run = null;
    }

    public void Move(JobState state, string message = "")
    {
        lock (_gate)
        {
            _state = state;
            _message = message;
            _blocker = null;
            if (state is JobState.Done or JobState.Failed or JobState.Cancelled) _finished = DateTime.UtcNow;
        }
    }

    public void Finish(DownloadResult? result, string message)
    {
        lock (_gate)
        {
            _result = result;
            _state = JobState.Done;
            _message = message;
            _blocker = null;
            _finished = DateTime.UtcNow;
        }
    }

    public void Report(DownloadProgress progress)
    {
        lock (_gate) _progress = progress;
    }

    public void Block(QueueTicket? blocker)
    {
        lock (_gate) _blocker = blocker;
    }

    public void Offer(UefnRelease? release)
    {
        lock (_gate) _uefn = release;
    }

    public void Note(string message)
    {
        lock (_gate) _message = message;
    }

    public bool Stop(bool cancel)
    {
        CancellationTokenSource? run;

        lock (_gate)
        {
            run = _run;
            if (run is null) return false;
            _cancelRequested = cancel;
        }

        try
        {
            run.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return true;
    }
}
