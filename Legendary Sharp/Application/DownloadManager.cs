using Legendary_Sharp.Downloader;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed class DownloadManager : IDisposable
{
    private static readonly TimeSpan TurnCheck = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(5);

    private readonly Session _session;
    private readonly Lock _gate = new();
    private readonly List<DownloadJob> _jobs = [];
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _runner;

    private int _version;

    public DownloadManager(Session session)
    {
        _session = session;
        _runner = Task.Run(RunAsync);
    }

    public int Version => Volatile.Read(ref _version);

    public IReadOnlyList<JobView> Snapshot()
    {
        lock (_gate) return [.. _jobs.Select(job => job.View())];
    }

    public bool HasActive => Snapshot().Any(job => job.IsActive);

    public bool Holds(string folder) => Any(folder, job => job.IsActive);

    public bool Claims(string folder) => Any(folder, job => !job.IsFinished);

    private bool Any(string folder, Func<JobView, bool> state, Guid except = default) =>
        Snapshot().Any(job => job.Id != except && state(job) && FolderPath.Same(job.Root, folder));

    public void Enqueue(DownloadJob job)
    {
        lock (_gate) _jobs.Add(job);
        Changed();
    }

    public void Pause(Guid id)
    {
        var job = Find(id);
        if (job is null) return;

        job.UserPaused = true;
        if (job.State is JobState.Running or JobState.Waiting && job.Stop(cancel: false)) return;
        if (job.State == JobState.Queued) job.Move(JobState.Paused);
        Changed();
    }

    public void Resume(Guid id)
    {
        var job = Find(id);
        if (job?.State is not (JobState.Paused or JobState.Failed or JobState.Cancelled)) return;

        job.Move(JobState.Queued);
        Changed();
    }

    public void CarryOn(Guid id)
    {
        var job = Find(id);
        if (job?.State != JobState.NeedsAttention) return;

        job.Options = job.Options with { SkipMissing = true };
        job.Move(JobState.Queued);
        Changed();
    }

    public async Task CancelAsync(Guid id, bool discard = false)
    {
        var job = Find(id);
        if (job is null || job.State is JobState.Done or JobState.Cancelled) return;

        if (job.State is JobState.Running or JobState.Waiting && job.Stop(cancel: true, discard)) return;

        await DiscardAsync(job, discard).ConfigureAwait(false);
        Changed();
    }

    private async Task DiscardAsync(DownloadJob job, bool discard)
    {
        if (!discard || !job.DiscardAllowed || Any(job.Root, other => other.IsActive, job.Id))
        {
            job.Move(JobState.Cancelled, "Cancelled, progress is kept in the folder.");
            return;
        }

        job.Note(DeletingMessage);
        Changed();
        var outcome = await Task.Run(() => InstallDiscard.Run(job.Request, job.Root)).ConfigureAwait(false);
        job.Move(JobState.Cancelled, DiscardMessage(outcome));
    }

    internal const string DeletingMessage = "Deleting the files it downloaded...";

    private static string DiscardMessage(DiscardOutcome outcome) => outcome.Failed > 0
        ? $"Cancelled, but {Format.Files(outcome.Failed)} {(outcome.Failed == 1 ? "is" : "are")} in use and left in the folder."
        : $"Cancelled, {Format.Files(outcome.Deleted)} deleted.";

    public void Remove(Guid id)
    {
        lock (_gate) _jobs.RemoveAll(job => job.Id == id && job.State is not (JobState.Running or JobState.Waiting));
        Changed();
    }

    public void ClearFinished()
    {
        lock (_gate) _jobs.RemoveAll(job => job.State is JobState.Done or JobState.Cancelled);
        Changed();
    }

    public void Shift(Guid id, int delta)
    {
        lock (_gate)
        {
            var index = _jobs.FindIndex(job => job.Id == id);
            if (index < 0) return;

            var target = Math.Clamp(index + delta, 0, _jobs.Count - 1);
            if (target == index) return;

            var job = _jobs[index];
            _jobs.RemoveAt(index);
            _jobs.Insert(target, job);
        }

        Changed();
    }

    private DownloadJob? Find(Guid id)
    {
        lock (_gate) return _jobs.FirstOrDefault(job => job.Id == id);
    }

    private DownloadJob? Next()
    {
        lock (_gate) return _jobs.FirstOrDefault(job => job.State == JobState.Queued);
    }

    private void Changed()
    {
        Interlocked.Increment(ref _version);

        List<DownloadJob> jobs;
        lock (_gate) jobs = [.. _jobs];

        QueueStore.Save(_session.Paths, jobs);
        _wake.Release();
    }

    private async Task RunAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            var job = Next();

            if (job is null)
            {
                Taskbar.Clear();

                try
                {
                    await _wake.WaitAsync(_shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            await RunJobAsync(job).ConfigureAwait(false);
        }
    }

    private async Task RunJobAsync(DownloadJob job)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        job.Begin(run);
        Changed();

        var request = job.Request;
        var manifest = request.Source.Manifest;

        try
        {
            Directory.CreateDirectory(job.Root);

            using var resume = ResumeLog.Open(InstallRecord.StateDirectory(job.Root),
                request.IsAddon ? ResumeLog.AddonLog : ResumeLog.BaseLog);

            using var lease = DownloadQueue.Join(_session.Paths, new QueueTicket
            {
                Build = job.Title,
                Folder = job.Root,
                Action = request.Action
            });

            await TakeTurnAsync(job, lease, run.Token).ConfigureAwait(false);

            if (request.RepairFiles is null)
                job.Plan = DownloadWorkflow.BuildPlan(request, manifest, job.Root, resume, announce: false);

            if (job.Plan.IsEmpty)
            {
                DownloadWorkflow.SaveRecord(request, job.Root, job.Plan, complete: true);
                job.Finish(null, "Every file was already in place.");
                await AfterAsync(job, run.Token).ConfigureAwait(false);
                return;
            }

            DownloadWorkflow.SaveRecord(request, job.Root, job.Plan, complete: false);
            lease.Start();
            job.Move(JobState.Running);
            Changed();

            var throttle = new Throttle(job.Options.Workers);
            using var monitor = _session.Settings.EaseOff ? LoadMonitor.Watch(throttle) : null;

            var result = await new DownloadEngine(job.Options)
                .RunAsync(job.Plan, resume, progress =>
                {
                    job.Report(progress);
                    lease.Report(progress);
                    Taskbar.Progress(progress.Fraction);
                }, run.Token, throttle)
                .ConfigureAwait(false);

            lease.Dispose();

            var complete = result.Failures.All(failure => failure.NotHosted);
            DownloadWorkflow.SaveRecord(request, job.Root, job.Plan, complete, result.Failures);
            job.Finish(result, result.Failures.Count == 0
                ? $"Finished in {Format.Duration(result.Elapsed)}."
                : $"{Format.Files(result.Failures.Count)} could not be downloaded, Epic no longer hosts them.");

            await AfterAsync(job, run.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            if (job.CancelRequested)
                await DiscardAsync(job, job.DiscardRequested).ConfigureAwait(false);
            else
            {
                job.Move(JobState.Paused, "Paused, progress is saved.");
                Taskbar.Paused(job.View().Fraction);
            }
        }
        catch (ChunkMissingException) when (!job.Options.SkipMissing)
        {
            job.Move(JobState.NeedsAttention,
                "Part of this build is missing from Epic's CDN. Carry on to get the rest and list what is missing.");
        }
        catch (Exception error)
        {
            ErrorLog.Write(error, $"{request.Action} {manifest.ShortVersion}");
            job.Move(JobState.Failed, error.Message);
            Taskbar.Error(job.View().Fraction);
        }
        finally
        {
            job.End();
            Changed();
        }
    }

    private static async Task TakeTurnAsync(DownloadJob job, QueueLease lease, CancellationToken cancellation)
    {
        while (true)
        {
            var status = lease.Check();

            if (status.IsMyTurn)
            {
                job.Block(null);
                return;
            }

            job.Block(status.Running ?? new QueueTicket { Build = "another build" });
            Taskbar.Waiting();
            await Task.Delay(TurnCheck, cancellation).ConfigureAwait(false);
        }
    }

    private async Task AfterAsync(DownloadJob job, CancellationToken cancellation)
    {
        var request = job.Request;

        if (request.IsAddon)
        {
            var editor = Path.Combine(job.Root, FortniteConstants.BinariesFolder, FortniteConstants.UefnExecutable);

            if (!File.Exists(editor))
                job.Note($"{FortniteConstants.UefnExecutable} is missing after the install, Epic no longer hosts all of it.");

            return;
        }

        if (request.RepairFiles is not null || !_session.Settings.OfferUefn) return;
        if (InstallRecord.TryLoad(job.Root)?.UefnInstalledUtc is not null) return;

        try
        {
            job.Offer(await UefnWorkflow.MatchAsync(_session, request.Source.Manifest.BuildVersion, cancellation)
                .ConfigureAwait(false));
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();

        try
        {
            _runner.Wait(ShutdownGrace);
        }
        catch (AggregateException)
        {
        }

        Taskbar.Clear();
        _shutdown.Dispose();
    }
}
