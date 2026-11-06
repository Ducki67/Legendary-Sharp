using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed class InteractiveShell(Session session)
{
    private const string VerifiedOnly = "confirmed";

    private readonly string? _here = LaunchFolder.Detect();

    private enum MainAction
    {
        Download,
        Downloads,
        Library,
        Browse,
        Settings,
        Quit
    }

    public async Task<int> RunAsync()
    {
        try
        {
            await new QueueRestore(session).RunAsync(Interrupt.Begin()).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        var jump = _here is not null;

        while (true)
        {
            Header();

            var action = MainAction.Download;
            if (!jump && !PickMain(out action))
            {
                if (ConfirmQuit()) return 0;
                continue;
            }

            jump = false;
            var cancellation = Interrupt.Begin();

            try
            {
                switch (action)
                {
                    case MainAction.Download:
                        await DownloadAsync(cancellation).ConfigureAwait(false);
                        break;
                    case MainAction.Downloads:
                        await new DownloadsScreen(session).RunAsync(null, cancellation).ConfigureAwait(false);
                        break;
                    case MainAction.Library:
                        await new LibraryScreen(session).RunAsync(cancellation).ConfigureAwait(false);
                        break;
                    case MainAction.Browse:
                        await new BrowseScreen(session, DownloadEntryAsync).RunAsync(cancellation).ConfigureAwait(false);
                        break;
                    case MainAction.Settings:
                        Settings();
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                Output.Blank();
                Output.Warn("Cancelled.");
                ConsoleEx.PauseForKey("Press any key to go back.");
            }
            catch (Exception error)
            {
                Output.Blank();
                Output.Error(error.Message);
                if (ErrorLog.Write(error, action.ToString()) is { } log) Output.Hint($"Details were saved to {log}");
                ConsoleEx.PauseForKey("Press any key to go back.");
            }
        }
    }

    public static void Header()
    {
        var lines = Logo.Lines(AppInfo.Tagline);
        lines.Add(string.Empty);
        ConsoleEx.Screen(lines);
    }

    private bool PickMain(out MainAction action)
    {
        var download = _here is null
            ? "pick a version and pull it from the CDN"
            : $"pick a version, it goes into {_here}";

        var choices = new List<Choice<MainAction>>
        {
            new(MainAction.Download, "Download a build", download),
            new(MainAction.Downloads, "Downloads", DownloadsNote()),
            new(MainAction.Library, "My builds", "verify, repair, resume, change tags, uninstall or add UEFN"),
            new(MainAction.Browse, "Browse releases", "every known build across the manifest archives"),
            new(MainAction.Settings, "Settings", "install folder, build folders, workers, default preset, mouse"),
            new(MainAction.Quit, "Quit", string.Empty)
        };

        return SelectionList<MainAction>.TryPick("What would you like to do?", choices, out action, banner: Banner)
               && action != MainAction.Quit;
    }

    private async Task DownloadAsync(CancellationToken cancellation)
    {
        if (_here is not null)
        {
            Output.Info($"New builds go into {_here}");
            Output.Blank();
        }

        var release = await PickReleaseAsync(cancellation).ConfigureAwait(false);
        if (release is null) return;

        await DownloadEntryAsync(release, cancellation).ConfigureAwait(false);
    }

    private async Task DownloadEntryAsync(ReleaseEntry release, CancellationToken cancellation)
    {
        var catalog = await session.CatalogAsync(false, cancellation).ConfigureAwait(false);

        ResolvedManifest resolved;
        using (Spinner.Start($"Reading the manifest for {release.Version}"))
        {
            resolved = await ManifestResolver
                .ResolveAsync(release.ManifestId, session.Http, session.Paths, catalog, cancellation)
                .ConfigureAwait(false);
        }

        var tags = TagPicker.Pick(resolved.Manifest);
        if (tags is null) return;

        var folder = PickFolder(release, resolved);
        if (folder is null) return;

        var request = new DownloadRequest
        {
            Source = resolved,
            InstallRoot = folder,
            Tags = tags
        };

        await new DownloadsScreen(session)
            .ShowQueuedAsync(await DownloadWorkflow.QueueAsync(session, request, cancellation).ConfigureAwait(false),
                cancellation)
            .ConfigureAwait(false);
    }

    private IReadOnlyList<string> Banner()
    {
        var jobs = session.Downloads.Snapshot();
        if (jobs.Count == 0) return [];

        var lines = new List<string>();
        var running = jobs.FirstOrDefault(job => job.State == JobState.Running);
        var queued = jobs.Count(job => job.State is JobState.Queued or JobState.Waiting);
        var bar = Math.Clamp(ConsoleEx.Width - 60, 10, 28);

        if (running is { Progress: { } progress })
        {
            lines.Add(Output.Indent + Theme.Emphasis("▌", Theme.Accent) + " " +
                      Theme.Paint("Downloading ", Theme.Dim) + Theme.Emphasis(running.Title, Theme.Cyan) + "  " +
                      Output.ProgressBar(progress.Fraction, bar) + "  " +
                      Theme.Emphasis(Format.Percent(progress.Fraction), Theme.Text) +
                      Theme.Paint($"  {Format.Duration(progress.Eta)} left", Theme.Muted));

            var extra = new List<string>();
            if (queued > 0) extra.Add(queued == 1 ? "1 more queued" : $"{queued} more queued");
            if (progress.Easing.Length > 0) extra.Add($"eased off, {progress.Easing}");
            if (extra.Count > 0) lines.Add(Output.Indent + Theme.Paint("  " + string.Join("  ·  ", extra), Theme.Muted));
        }
        else if (jobs.FirstOrDefault(job => job.State == JobState.Waiting) is { } waiting)
        {
            lines.Add(Output.Indent + Theme.Paint("◌ ", Theme.Muted) +
                      Theme.Paint($"{waiting.Title} is waiting for another window to finish " +
                                  $"{waiting.Blocker?.Build ?? "its download"}", Theme.Dim));
        }
        else if (running is not null || queued > 0)
        {
            lines.Add(Output.Indent + Theme.Paint("◌ ", Theme.Muted) + Theme.Paint("Starting the next download", Theme.Dim));
        }

        if (jobs.FirstOrDefault(job => job.State == JobState.NeedsAttention) is { } attention)
            lines.Add(Output.Indent + Theme.Emphasis("! ", Theme.Warning) +
                      Theme.Paint($"{attention.Title} needs you, part of it is missing. Open Downloads.", Theme.Warning));

        if (jobs.FirstOrDefault(job => job.State == JobState.Failed) is { } failed)
            lines.Add(Output.Indent + Theme.Emphasis("✗ ", Theme.Danger) +
                      Theme.Paint($"{failed.Title} failed. Open Downloads to try again.", Theme.Danger));

        var finished = jobs
            .Where(job => job is { State: JobState.Done, Finished: { } at } && DateTime.UtcNow - at < TimeSpan.FromMinutes(30))
            .MaxBy(job => job.Finished);

        if (finished is not null)
            lines.Add(Output.Indent + (finished.IsComplete
                ? Theme.Emphasis("✓ ", Theme.Success) + Theme.Paint($"{finished.Title} finished", Theme.Success)
                : Theme.Emphasis("! ", Theme.Warning) + Theme.Paint($"{finished.Title} finished with files missing", Theme.Warning)) +
                (finished.Uefn is not null ? Theme.Paint("  ·  UEFN is available for it in Downloads", Theme.Cyan) : string.Empty));

        var paused = jobs.Count(job => job.State == JobState.Paused);
        if (paused > 0 && running is null)
            lines.Add(Output.Indent + Theme.Paint("‖ ", Theme.Warning) +
                      Theme.Paint(paused == 1 ? "1 download is paused" : $"{paused} downloads are paused", Theme.Dim));

        if (lines.Count > 0) lines.Add(string.Empty);
        return lines;
    }

    private string DownloadsNote()
    {
        var jobs = session.Downloads.Snapshot();
        var active = jobs.Count(job => job.IsActive);
        var paused = jobs.Count(job => job.State is JobState.Paused or JobState.NeedsAttention);

        if (active == 0 && paused == 0)
            return jobs.Count == 0 ? "watch, pause and reorder what is downloading" : "nothing running right now";

        var parts = new List<string>();
        if (active > 0) parts.Add(active == 1 ? "1 in the queue" : $"{active} in the queue");
        if (paused > 0) parts.Add($"{paused} waiting on you");
        return string.Join(", ", parts);
    }

    private bool ConfirmQuit()
    {
        var active = session.Downloads.Snapshot().Count(job => job.IsActive);
        if (active == 0) return true;

        Output.Blank();
        Output.Warn(active == 1 ? "A download is still going." : $"{active} downloads are still going.");
        Output.Hint("Progress is saved, and they are offered again the next time you open the app.");
        Output.Blank();
        return Prompt.Ask("Quit anyway?", false) == true;
    }

    private async Task<ReleaseEntry?> PickReleaseAsync(CancellationToken cancellation)
    {
        var catalog = await Spinner
            .Run("Loading the release index", () => session.CatalogAsync(false, cancellation))
            .ConfigureAwait(false);

        var downloadable = catalog.Downloadable.ToList();

        if (downloadable.Count == 0)
        {
            Output.Error("The release index has no downloadable builds.");
            ConsoleEx.PauseForKey("Press any key to go back.");
            return null;
        }

        var confirmed = downloadable.Where(entry => entry.IsVerified).ToList();

        var seasons = new List<Choice<string>>
        {
            new(VerifiedOnly, "Confirmed complete",
                $"{Format.Count(confirmed.Count)} builds known to download in full"),
            new(string.Empty, "All builds", $"{Format.Count(downloadable.Count)} with a manifest")
        };

        seasons.AddRange(catalog.Seasons
            .Select(season => new
            {
                Season = season,
                Builds = downloadable.Where(entry => entry.Season == season).ToList()
            })
            .Where(item => item.Builds.Count > 0)
            .Select(item => new Choice<string>(item.Season, item.Season, SeasonNote(item.Builds))));

        if (!SelectionList<string>.TryPick("Which season?", seasons, out var chosenSeason)) return null;

        var pool = chosenSeason switch
        {
            VerifiedOnly => confirmed,
            "" => downloadable,
            _ => [.. downloadable.Where(entry => entry.Season == chosenSeason)]
        };

        var builds = pool
            .Select(entry => new Choice<ReleaseEntry>(
                entry,
                entry.Version,
                Describe(entry)))
            .ToList();

        return SelectionList<ReleaseEntry>.TryPick("Which build?", builds, out var release) ? release : null;
    }

    private static string SeasonNote(IReadOnlyList<ReleaseEntry> builds)
    {
        var count = $"{Format.Count(builds.Count)} builds";
        var confirmed = builds.Count(entry => entry.IsVerified);

        if (confirmed > 0) return $"{count}   ✓ {confirmed} confirmed";
        return builds.All(entry => entry.IsKnownPatchy) ? $"{count}   ! none download in full" : count;
    }

    private static string Describe(ReleaseEntry entry)
    {
        var parts = new List<string>();

        if (entry.IsVerified) parts.Add("✓ confirmed complete");
        else if (entry.IsKnownPatchy) parts.Add("! " + (VerifiedBuilds.MissingNote(entry.Version) ?? "expect missing files"));

        if (entry.BuildDate.Length > 0) parts.Add(entry.BuildDate);
        if (entry.EngineVersion.Length > 0) parts.Add("UE " + entry.EngineVersion);
        if (entry.Notes.Length > 0) parts.Add(entry.Notes);
        return string.Join("   ", parts);
    }

    private string? PickFolder(ReleaseEntry release, ResolvedManifest resolved)
    {
        var root = _here ?? session.Settings.EffectiveInstallRoot;

        var suggestion = Path.Combine(root,
            Naming.SanitiseFolder(release.SuggestedFolderName.Length > 0
                ? release.SuggestedFolderName
                : resolved.Manifest.ShortVersion));

        if (_here is not null) return suggestion;

        Output.Blank();
        return Prompt.Path("Install folder", suggestion);
    }

    private void Settings() => new SettingsScreen(session).Run();
}
