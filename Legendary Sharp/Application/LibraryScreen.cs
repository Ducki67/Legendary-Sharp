using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed class LibraryScreen(Session session)
{
    private enum BuildAction
    {
        Verify,
        Repair,
        Resume,
        Uefn,
        Tags,
        Uninstall,
        Reveal,
        Add,
        Back
    }

    private sealed record LibraryRow(InstalledBuild? Managed, UnmanagedBuild? Other, BuildProbe? Probe);

    public async Task RunAsync(CancellationToken cancellation)
    {
        var roots = session.Settings.ScanRoots().ToList();
        IReadOnlyList<InstalledBuild> builds;
        IReadOnlyList<UnmanagedBuild> unmanaged;

        using (Spinner.Start("Looking for builds"))
        {
            (builds, unmanaged) = await Task.Run(() => BuildLibrary.ScanAll(roots), cancellation)
                .ConfigureAwait(false);
        }

        if (builds.Count == 0 && unmanaged.Count == 0)
        {
            Output.Blank();
            Output.Info("No builds found yet.");
            Output.Hint($"Looked in {string.Join(", ", roots)}. Builds added from existing files show up here too.");
            ConsoleEx.PauseForKey("Press any key to go back.");
            return;
        }

        var queue = DownloadQueue.Others(session.Paths);
        var mine = session.Downloads.Snapshot();

        var rows = builds
            .Select(build => new Choice<LibraryRow>(
                new LibraryRow(build, null, null),
                Naming.ShortenBuildVersion(build.Record.BuildVersion),
                $"{Format.Bytes(build.Record.InstallSize)}  {Status(build, queue, mine)}" +
                $"{(build.Record.Adopted ? "  added from existing files" : string.Empty)}  {build.Path}"))
            .ToList();

        if (unmanaged.Count > 0)
        {
            IReadOnlyDictionary<string, BuildProbe?> probes;

            using (Spinner.Start("Reading build versions"))
            {
                probes = await Task.Run(() => BuildProbes.ReadAll(unmanaged.Select(other => other.Path), session.Paths),
                    cancellation).ConfigureAwait(false);
            }

            foreach (var other in unmanaged)
            {
                var probe = probes.GetValueOrDefault(other.Path);

                rows.Add(new Choice<LibraryRow>(
                    new LibraryRow(null, other, probe),
                    probe is null ? "unknown build" : Naming.ShortenBuildVersion(probe.Version),
                    $"not added  {other.Path}"));
            }
        }

        if (!SelectionList<LibraryRow>.TryPick("Which build?", rows, out var row)) return;

        if (row.Other is { } folder)
        {
            await UnmanagedAsync(folder, row.Probe, cancellation).ConfigureAwait(false);
            return;
        }

        var chosen = row.Managed!;
        OpenedBuild? opened = null;
        var downloadable = !chosen.Record.IsComplete;

        if (downloadable)
        {
            try
            {
                opened = await OpenBuildAsync(chosen, cancellation).ConfigureAwait(false);
                downloadable = HasDownloadable(opened);
            }
            catch (Exception error) when (IsLoadFailure(error, cancellation))
            {
            }
        }

        var actions = new List<Choice<BuildAction>>
        {
            new(BuildAction.Verify, "Verify", "hash every file against the manifest"),
            new(BuildAction.Repair, "Repair", "verify then re-download anything broken"),
            new(BuildAction.Uefn, "Install UEFN",
                chosen.Record.HasUefn ? "already installed, run again to restore files" : "add the Unreal Editor"),
            new(BuildAction.Tags, "Change install tags", "add or remove parts of the build"),
            new(BuildAction.Uninstall, "Uninstall", "delete only the files this app installed"),
            new(BuildAction.Reveal, "Show folder path", chosen.Path),
            new(BuildAction.Back, "Back", string.Empty)
        };

        if (downloadable)
            actions.Insert(2, new(BuildAction.Resume, "Resume download", "continue an interrupted download"));

        if (!SelectionList<BuildAction>.TryPick(Naming.ShortenBuildVersion(chosen.Record.BuildVersion), actions,
                out var action)) return;

        switch (action)
        {
            case BuildAction.Back:
                return;

            case BuildAction.Reveal:
                Output.Blank();
                Output.Pair("Folder", chosen.Path);
                break;

            case BuildAction.Verify or BuildAction.Repair when IsBusy(chosen,
                "Checking it now would hash files that are still being written. Try again once it finishes.",
                "Checking it now would hash files that are still being written. Try again once that finishes."):
                break;

            case BuildAction.Tags or BuildAction.Uninstall when IsBusy(chosen,
                "Files are still being written here, so this build can't be changed right now.",
                "Stop or finish the download in Downloads, then try again."):
                break;

            case BuildAction.Tags:
            {
                opened ??= await TryOpenBuildAsync(chosen, cancellation).ConfigureAwait(false);
                if (opened is not null && await ChangeTagsAsync(opened, cancellation).ConfigureAwait(false)) return;
                break;
            }

            case BuildAction.Uninstall:
            {
                opened ??= await TryOpenBuildAsync(chosen, cancellation).ConfigureAwait(false);
                if (opened is not null) await UninstallAsync(opened, cancellation).ConfigureAwait(false);
                break;
            }

            case BuildAction.Verify:
            {
                opened ??= await OpenBuildAsync(chosen, cancellation).ConfigureAwait(false);
                await VerifyWorkflow.RunAsync(opened, cancellation).ConfigureAwait(false);
                break;
            }

            case BuildAction.Repair:
            {
                var job = await RepairWorkflow.RunAsync(session, chosen.Path, cancellation).ConfigureAwait(false);
                if (job is null) break;
                await ShowQueuedAsync(job, cancellation).ConfigureAwait(false);
                return;
            }

            case BuildAction.Uefn:
                await ShowQueuedAsync(
                    await UefnWorkflow.InstallForBuildAsync(session, chosen.Path, cancellation).ConfigureAwait(false),
                    cancellation).ConfigureAwait(false);
                return;

            case BuildAction.Resume:
            {
                opened ??= await OpenBuildAsync(chosen, cancellation).ConfigureAwait(false);

                await ShowQueuedAsync(await DownloadWorkflow.QueueAsync(session, new DownloadRequest
                {
                    Source = opened.Source,
                    InstallRoot = opened.Root,
                    Tags = opened.Record.TagFilter
                }, cancellation).ConfigureAwait(false), cancellation).ConfigureAwait(false);

                return;
            }
        }

        ConsoleEx.PauseForKey("Press any key to go back to the menu.");
    }

    private static bool IsLoadFailure(Exception error, CancellationToken cancellation) =>
        error is IOException or InvalidOperationException or UnauthorizedAccessException or HttpRequestException
            or InvalidDataException
        || error is TaskCanceledException && !cancellation.IsCancellationRequested;

    private Task ShowQueuedAsync(DownloadJob? job, CancellationToken cancellation) =>
        new DownloadsScreen(session).ShowQueuedAsync(job, cancellation);

    private Task<OpenedBuild> OpenBuildAsync(InstalledBuild chosen, CancellationToken cancellation) =>
        Spinner.Run($"Opening {Naming.ShortenBuildVersion(chosen.Record.BuildVersion)}",
            () => InstalledBuildLoader.OpenAsync(session, chosen.Path, cancellation));

    private async Task<OpenedBuild?> TryOpenBuildAsync(InstalledBuild chosen, CancellationToken cancellation)
    {
        try
        {
            return await OpenBuildAsync(chosen, cancellation).ConfigureAwait(false);
        }
        catch (Exception error) when (IsLoadFailure(error, cancellation))
        {
            Output.Blank();
            Output.Error(error.Message);
            return null;
        }
    }

    private static bool HasDownloadable(OpenedBuild build)
    {
        if (build.Record.IsComplete) return false;

        var missing = build.Record.UnavailableSet();

        foreach (var file in InstalledBuildLoader.SelectedFiles(build))
        {
            if (missing.Contains(file.FileName)) continue;

            var info = new FileInfo(Path.Combine(build.Root, file.ToNativePath()));
            if (!info.Exists || info.Length != file.Size) return true;
        }

        return false;
    }

    private static string Status(InstalledBuild build, IReadOnlyList<QueueEntry> queue, IReadOnlyList<JobView> mine)
    {
        if (mine.FirstOrDefault(job => job.IsActive && FolderPath.Same(job.Root, build.Path)) is { } job)
            return job.State == JobState.Running ? "downloading now" : "in your downloads";

        for (var index = 0; index < queue.Count; index++)
        {
            if (queue[index].Ticket?.IsFor(build.Path) != true) continue;
            return index == 0 ? "downloading in another window" : "queued in another window";
        }

        return build.Status;
    }

    private bool IsBusy(InstalledBuild build, string hint, string otherWindowHint)
    {
        if (session.Downloads.Claims(build.Path))
        {
            Output.Blank();
            Output.Warn("This build is in your downloads right now.");
            Output.Hint(hint);
            return true;
        }

        var ticket = DownloadQueue.Busy(session.Paths, build.Path);
        if (ticket is null) return false;

        Output.Blank();
        Output.Warn($"Another window is working on this folder right now ({ticket.Action.ToLowerInvariant()} of {ticket.Build}).");
        Output.Hint(otherWindowHint);
        return true;
    }

    private async Task<BuildManifest?> LoadAddonAsync(OpenedBuild build, CancellationToken cancellation)
    {
        if (!build.Record.HasUefn) return null;

        using var spinner = Spinner.Start("Reading the UEFN files");

        try
        {
            var local = InstallRecord.UefnManifestPath(build.Root);

            if (File.Exists(local))
                return await ManifestLoader.LoadFileAsync(local, cancellation).ConfigureAwait(false);

            if (build.Record.UefnManifestId.Length == 0) return null;

            var resolved = await ManifestResolver
                .ResolveAsync(build.Record.UefnManifestId, session.Http, session.Paths, null, cancellation)
                .ConfigureAwait(false);

            return resolved.Manifest;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<bool> ChangeTagsAsync(OpenedBuild build, CancellationToken cancellation)
    {
        var manifest = build.Manifest;
        var groups = InstallTagCatalog.Summarise(manifest);

        if (groups.Count <= 1)
        {
            Output.Blank();
            Output.Info("This build has no install tags.");
            return false;
        }

        var old = build.Record.TagFilter;
        var picked = TagPicker.Pick(manifest, old ?? InstallTagCatalog.Flatten(groups));
        if (picked is null) return false;

        var hasUntagged = manifest.Files.Any(file => file.InstallTags.Length == 0);

        if (hasUntagged && !picked.Contains(string.Empty))
        {
            picked.Add(string.Empty);
            Output.Blank();
            Output.Hint("The untagged files are always kept.");
        }

        var unavailable = build.Record.UnavailableSet();
        List<FileEntry> added;
        List<FileEntry> removed;
        var absent = 0;
        var absentUnavailable = 0;
        long removedSize = 0;

        using (Spinner.Start("Comparing install tags"))
        {
            var before = DownloadPlanner.SelectFiles(manifest, old);
            var after = DownloadPlanner.SelectFiles(manifest, picked);
            var kept = before.Select(file => file.FileName).ToHashSet(StringComparer.Ordinal);
            added = after.Where(file => !kept.Contains(file.FileName) && !unavailable.Contains(file.FileName)).ToList();
            removed = [];

            foreach (var file in before.Where(file => !DownloadPlanner.Matches(file, picked)))
            {
                var info = new FileInfo(Path.Combine(build.Root, file.ToNativePath()));

                if (info.Exists)
                {
                    removed.Add(file);
                    removedSize += info.Length;
                }
                else
                {
                    absent++;
                    if (unavailable.Contains(file.FileName)) absentUnavailable++;
                }
            }
        }

        Output.Blank();

        if (added.Count == 0 && removed.Count == 0)
        {
            build.Record.InstallTags = picked;
            build.Record.InstallSize = InstallTagCatalog.SizeOf(manifest, picked);
            build.Record.Save(build.Root);

            if (absent == 0)
            {
                Output.Info("Nothing changed.");
                return false;
            }

            Output.Success("Updated the install tags.");
            return false;
        }

        Output.Pair("Add", $"{Format.Files(added.Count)}, {Format.Bytes(added.Sum(file => file.Size))}");
        Output.Pair("Remove", $"{Format.Files(removed.Count)}, {Format.Bytes(removedSize)}");

        if (absent > 0)
            Output.Hint(absentUnavailable == absent
                ? $"{Format.Files(absent)} more {(absent == 1 ? "is" : "are")} part of the removed tags, but Epic no longer hosts {(absent == 1 ? "it" : "them")}."
                : $"{Format.Files(absent)} more {(absent == 1 ? "is" : "are")} part of the removed tags, but {(absent == 1 ? "was" : "were")} never downloaded.");

        Output.Blank();

        BuildManifest? addon = null;

        if (removed.Count > 0)
        {
            addon = await LoadAddonAsync(build, cancellation).ConfigureAwait(false);

            if (build.Record.HasUefn && addon is null)
            {
                Output.Warn("The UEFN files could not be read, so nothing was removed to keep them safe.");
                return false;
            }
        }

        if (Prompt.Ask("Apply these changes?", true) != true) return false;

        if (removed.Count > 0)
        {
            DiscardOutcome outcome;

            using (Spinner.Start("Deleting the files of the removed tags"))
            {
                outcome = await Task.Run(() => InstallRemoval.RemoveTags(build.Root, removed, addon, cancellation),
                    cancellation).ConfigureAwait(false);
            }

            Output.Blank();

            if (outcome.Failed > 0)
            {
                Output.Warn(outcome.Describe("Removed"));
                Output.Hint("The install tags were not changed. Close whatever is using those files and try again.");
                return false;
            }

            Output.Success(outcome.Describe("Removed"));
        }

        build.Record.InstallTags = picked;
        build.Record.InstallSize = InstallTagCatalog.SizeOf(manifest, picked);

        if (added.Count > 0)
        {
            build.Record.CompletedUtc = null;
            build.Record.Extended = true;
        }

        build.Record.Save(build.Root);

        if (added.Count == 0) return false;

        await ShowQueuedAsync(await DownloadWorkflow.QueueAsync(session, new DownloadRequest
        {
            Source = build.Source,
            InstallRoot = build.Root,
            Tags = picked,
            AssumeYes = true
        }, cancellation).ConfigureAwait(false), cancellation).ConfigureAwait(false);

        return true;
    }

    private async Task UnmanagedAsync(UnmanagedBuild folder, BuildProbe? probe, CancellationToken cancellation)
    {
        var actions = new List<Choice<BuildAction>>
        {
            new(BuildAction.Add, "Add to " + AppInfo.Name, probe is null
                ? "the version of this build could not be read"
                : "check every file, then manage it here", probe is not null),
            new(BuildAction.Reveal, "Show folder path", folder.Path),
            new(BuildAction.Back, "Back", string.Empty)
        };

        if (!SelectionList<BuildAction>.TryPick(
                probe is null ? "Unknown build" : Naming.ShortenBuildVersion(probe.Version), actions,
                out var action)) return;

        switch (action)
        {
            case BuildAction.Reveal:
                Output.Blank();
                Output.Pair("Folder", folder.Path);
                break;

            case BuildAction.Add when probe is not null:
                await AddExistingAsync(folder, probe, cancellation).ConfigureAwait(false);
                break;

            default:
                return;
        }

        ConsoleEx.PauseForKey("Press any key to go back to the menu.");
    }

    private async Task AddExistingAsync(UnmanagedBuild folder, BuildProbe probe, CancellationToken cancellation)
    {
        IReadOnlyList<BuildMatch> matches;

        try
        {
            using (Spinner.Start($"Matching {probe.Version}"))
            {
                matches = await BuildMatcher.MatchAsync(session, folder.Path, probe, cancellation)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception error) when (IsLoadFailure(error, cancellation))
        {
            Output.Blank();
            Output.Error(error.Message);
            return;
        }

        var accepted = matches.Where(match => match.Accepted).ToList();

        if (accepted.Count == 0)
        {
            Output.Blank();
            Output.Warn("This folder does not match any known build closely enough. Nothing was added.");
            return;
        }

        var best = accepted[0];
        var ties = accepted.Where(match => match.Present == best.Present && match.Total == best.Total).ToList();

        if (ties.Count > 1)
        {
            var choices = ties
                .Select(match => new Choice<BuildMatch>(
                    match,
                    match.Release?.Version ?? match.Source.Manifest.ShortVersion,
                    match.Source.ManifestId))
                .ToList();

            if (!SelectionList<BuildMatch>.TryPick("Which build is this?", choices, out best)) return;
        }

        var manifest = best.Source.Manifest;
        var groups = best.Tags.Count(tag => tag.Present > 0);

        var found = string.Join(", ", best.Tags
            .Where(tag => tag.Present > 0)
            .Take(6)
            .Select(tag => $"{tag.Group.Display} {Format.Percent(tag.Total == 0 ? 0d : (double)tag.Present / tag.Total)}"));

        Output.Section("Add existing build");

        new Panel()
            .Row("Version", best.Release?.Version ?? manifest.ShortVersion, Theme.Cyan)
            .Row("Changelist", probe.Changelist ?? "unknown")
            .Row("Folder", folder.Path)
            .Row("Tags found", $"{groups} of {best.Tags.Count} groups" + (found.Length > 0 ? $"  ({found})" : string.Empty))
            .Row("Files present", $"{Format.Count(best.Present)} of {Format.Count(best.Total)}")
            .Row("Size on disk", Format.Bytes(FolderSize(folder.Path)))
            .Render();

        if (File.Exists(Path.Combine(folder.Path, FortniteConstants.BinariesFolder, FortniteConstants.UefnExecutable)))
            Output.Hint("UEFN files found next to it are not added.");

        if (!best.ExeMatches)
        {
            Output.Blank();

            if (Prompt.Ask(
                    "The game executable differs from the original (patched?). Repair would replace it. Add it anyway?",
                    false) != true) return;
        }

        var tags = TagPicker.Pick(manifest, best.SuggestedTags);
        if (tags is null) return;

        Output.Blank();

        if (Prompt.Ask($"Add this build to {AppInfo.Name}? Every file is hashed first and nothing is changed or deleted.",
                true) != true) return;

        var result = await BuildAdoption.AdoptAsync(session, folder.Path, best, tags, cancellation)
            .ConfigureAwait(false);

        Output.Blank();

        if (!result.Adopted)
        {
            Output.Warn(result.Refusal ?? "Nothing was added.");
            return;
        }

        Output.Success(
            $"Added. {Format.Count(result.Healthy)} {(result.Healthy == 1 ? "file matches" : "files match")}, " +
            $"{Format.Count(result.Missing)} missing, {Format.Count(result.Damaged)} damaged.");

        if (result.Missing + result.Damaged > 0)
            Output.Hint("Resume download or Repair in My builds can fill the gaps.");
    }

    private static long FolderSize(string path)
    {
        long total = 0;

        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
                total += file.Length;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }

        return total;
    }

    private async Task UninstallAsync(OpenedBuild build, CancellationToken cancellation)
    {
        var addon = await LoadAddonAsync(build, cancellation).ConfigureAwait(false);

        if (build.Record.HasUefn && addon is null)
        {
            Output.Blank();
            Output.Warn("The UEFN manifest could not be read, so the UEFN files can't be identified.");

            if (Prompt.Ask("Uninstall without removing the UEFN files?", false) != true) return;
        }

        var files = InstalledBuildLoader.SelectedFiles(build);
        var addonFiles = addon?.Files.ToList();
        var count = files.Count + (addonFiles?.Count ?? 0);
        var size = files.Sum(file => file.Size) + (addonFiles?.Sum(file => file.Size) ?? 0);

        Output.Blank();
        Output.Pair("Files", Format.Count(count));
        Output.Pair("Size", Format.Bytes(size));
        Output.Hint("Other files in the folder are kept. The folder itself is never deleted.");

        if (build.Record.Adopted)
            Output.Hint($"This build was added from existing files, not downloaded by {AppInfo.Name}.");

        Output.Blank();

        if (Prompt.Ask($"Delete the files {AppInfo.Name} installed in {build.Root}?", false) != true) return;

        DiscardOutcome outcome;

        using (Spinner.Start("Deleting the installed files"))
        {
            outcome = await Task.Run(() => InstallRemoval.Uninstall(build.Root, files, addonFiles, cancellation),
                cancellation).ConfigureAwait(false);
        }

        Output.Blank();

        if (outcome.Failed == 0) Output.Success(outcome.Describe("Deleted"));
        else Output.Warn(outcome.Describe("Deleted"));
    }
}
