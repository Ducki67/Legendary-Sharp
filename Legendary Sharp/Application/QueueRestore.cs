using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed class QueueRestore(Session session)
{
    public async Task RunAsync(CancellationToken cancellation)
    {
        var taken = QueueStore.TakeAbandoned(session.Paths, out var claims);
        var saved = taken.Where(job => !job.Paused).ToList();
        var paused = taken.Where(job => job.Paused).ToList();

        if (saved.Count > 0)
        {
            InteractiveShell.Header();
            Output.Section("Unfinished downloads");
            Output.Info(saved.Count == 1
                ? "A download was still queued when the app last closed."
                : $"{saved.Count} downloads were still queued when the app last closed.");
            Output.Blank();

            foreach (var job in saved.Take(8)) Output.Bullet($"{job.Build}   {job.Root}");
            if (saved.Count > 8) Output.Detail($"and {saved.Count - 8} more");

            Output.Blank();

            if (Prompt.Ask("Pick them back up?") == true)
            {
                foreach (var job in saved) await RestoreJobAsync(job, false, cancellation).ConfigureAwait(false);
            }
            else if (Prompt.Ask("Delete the files they created so far? Files that were already there are kept.", false) == true)
            {
                await DiscardSavedAsync(saved, paused, cancellation).ConfigureAwait(false);
            }
        }

        if (paused.Count > 0)
        {
            if (saved.Count == 0)
            {
                InteractiveShell.Header();
                Output.Section("Unfinished downloads");
            }

            Output.Info("Restoring your downloads...");
        }

        foreach (var job in paused) await RestoreJobAsync(job, true, cancellation).ConfigureAwait(false);

        QueueStore.Release(claims);
    }

    private async Task RestoreJobAsync(SavedJob job, bool paused, CancellationToken cancellation)
    {
        try
        {
            var source = await RestoreSourceAsync(job, cancellation).ConfigureAwait(false);
            var restored = DownloadWorkflow.CreateQuietly(session, RestoreRequest(job, source));
            if (paused)
            {
                restored.Move(JobState.Paused, "Paused, progress is saved.");
                restored.UserPaused = true;
            }
            session.Downloads.Enqueue(restored);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            Output.Warn($"{job.Build} could not be picked back up: {error.Message}");
            ErrorLog.Write(error, $"restore {job.Build}");
        }
    }

    private static DownloadRequest RestoreRequest(SavedJob job, ResolvedManifest source) => new()
    {
        Source = source,
        InstallRoot = job.Root,
        Tags = job.Tags,
        IsAddon = job.IsAddon,
        SkipMissing = job.SkipMissing
    };

    private async Task DiscardSavedAsync(List<SavedJob> saved, List<SavedJob> paused, CancellationToken cancellation)
    {
        var skipped = 0;
        var prepared = new List<(SavedJob Job, DownloadRequest Request)>();

        foreach (var job in saved)
        {
            try
            {
                var request = RestoreRequest(job, await RestoreSourceAsync(job, cancellation).ConfigureAwait(false));
                if (InstallDiscard.IsAllowed(request, job.Root)) prepared.Add((job, request));
                else skipped++;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                skipped++;
                Output.Warn($"{job.Build} could not be cleaned up: {error.Message}");
                ErrorLog.Write(error, $"discard {job.Build}");
            }
        }

        var deleted = 0;
        var failed = 0;

        foreach (var (job, request) in prepared)
        {
            if (session.Downloads.Holds(job.Root) || DownloadQueue.Busy(session.Paths, job.Root) is not null ||
                paused.Any(other => FolderPath.Same(other.Root, job.Root)))
            {
                skipped++;
                continue;
            }

            DiscardOutcome outcome;

            using (Spinner.Start($"Deleting the files {job.Build} created"))
                outcome = await Task.Run(() => InstallDiscard.Run(request, job.Root), cancellation).ConfigureAwait(false);

            deleted += outcome.Deleted;
            failed += outcome.Failed;
        }

        Output.Success($"Deleted {Format.Count(deleted)} files.");
        if (failed > 0) Output.Warn($"{Format.Count(failed)} files are in use and were left in the folder.");
        if (skipped > 0) Output.Hint(skipped == 1 ? "1 download was left as it is." : $"{skipped} downloads were left as they are.");
    }

    private async Task<ResolvedManifest> RestoreSourceAsync(SavedJob job, CancellationToken cancellation)
    {
        var stored = job.IsAddon ? InstallRecord.UefnManifestPath(job.Root) : InstallRecord.ManifestPath(job.Root);

        if (File.Exists(stored))
            return new ResolvedManifest(
                await ManifestLoader.LoadFileAsync(stored, cancellation).ConfigureAwait(false),
                job.ManifestId, "install folder", null);

        using (Spinner.Start($"Reading the manifest for {job.Build}"))
        {
            return await ManifestResolver
                .ResolveAsync(job.ManifestId, session.Http, session.Paths, null, cancellation)
                .ConfigureAwait(false);
        }
    }
}
