using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed record DownloadRequest
{
    public required ResolvedManifest Source { get; init; }

    public required string InstallRoot { get; init; }

    public required IReadOnlyList<string>? Tags { get; init; }

    public int? Workers { get; init; }

    public int? CacheBudgetMiB { get; init; }

    public bool AssumeYes { get; init; }

    public bool Fresh { get; init; }

    public bool SkipMissing { get; init; }

    public bool IsAddon { get; init; }

    public IReadOnlyList<FileEntry>? RepairFiles { get; init; }

    public string Action => RepairFiles is not null ? "Repair" : IsAddon ? "Install UEFN" : "Download";
}

internal static class DownloadWorkflow
{
    public static async Task<DownloadJob?> QueueAsync(Session session, DownloadRequest request, CancellationToken cancellation)
    {
        var job = await PrepareAsync(session, request, cancellation).ConfigureAwait(false);
        if (job is null) return null;

        session.Downloads.Enqueue(job);
        return job;
    }

    public static async Task<DownloadJob?> PrepareAsync(Session session, DownloadRequest request, CancellationToken cancellation)
    {
        var manifest = request.Source.Manifest;
        var root = Path.GetFullPath(request.InstallRoot);

        if (request.Tags is { Count: 0 })
            throw new InvalidOperationException("No install tags were selected, so there is nothing to download.");

        if (session.Downloads.Holds(root))
        {
            Output.Blank();
            Output.Warn("This folder is already in your downloads.");
            Output.Hint("Open Downloads to see it, or pick another folder.");
            return null;
        }

        if (!ConfirmFolder(request, manifest, root))
        {
            Output.Hint("Cancelled.");
            return null;
        }

        Directory.CreateDirectory(root);

        DownloadPlan plan;
        using (var resume = ResumeLog.Open(InstallRecord.StateDirectory(root), request.IsAddon ? ResumeLog.AddonLog : ResumeLog.BaseLog))
        {
            if (request.Fresh) resume.Clear();
            plan = BuildPlan(request, manifest, root, resume, announce: true);
        }

        var options = session.BuildDownloadOptions(root, request.Workers, request.CacheBudgetMiB);

        Summarise(request, plan, root, options);

        if (plan.IsEmpty)
        {
            Output.Blank();
            Output.Success("Nothing to do, every selected file is already in place.");
            SaveRecord(request, root, plan, complete: true);
            return null;
        }

        if (!HasRoomOnDisk(root, plan, out var free))
        {
            Output.Blank();
            Output.Error($"Not enough free space: {Format.Bytes(plan.WriteSize)} needed, {Format.Bytes(free)} available.");
            return null;
        }

        var skipMissing = request.SkipMissing;

        if (!request.IsAddon && VerifiedBuilds.IsKnownPatchy(request.Source.Release?.Version ?? manifest.ShortVersion))
        {
            Output.Blank();
            Output.Warn(KnownMissing(request, manifest) is { } files
                ? $"Epic no longer hosts all of this build, {files} {(files == 1 ? "file" : "files")} will be missing with these tags."
                : "Epic no longer hosts all of this build, it will be missing files whatever tags you pick.");
            Output.Detail("Only the builds marked confirmed complete download in full, see docs/BUILDS.md.");
        }

        var availability = await ProbeAsync(session, plan, cancellation).ConfigureAwait(false);

        if (availability.IsOffline) return null;

        if (!availability.IsComplete)
        {
            if (availability.IsHopeless)
            {
                Output.Blank();
                Output.Error("None of the sampled chunks are on Epic's CDN any more, this build cannot be downloaded.");
                return null;
            }

            if (!skipMissing)
            {
                Output.Blank();
                skipMissing = Prompt.Ask("Download what is still there and list the rest?", false) == true;

                if (!skipMissing)
                {
                    Output.Hint("Cancelled.");
                    return null;
                }
            }
        }

        var busy = AnnounceQueue(session);

        if (!request.AssumeYes && ConsoleEx.IsInteractive)
        {
            Output.Blank();
            var question = busy
                ? $"Add the {request.Action.ToLowerInvariant()} to your downloads?"
                : $"Start the {request.Action.ToLowerInvariant()}?";

            if (Prompt.Ask(question) != true)
            {
                Output.Hint("Cancelled.");
                return null;
            }
        }

        return new DownloadJob(request, root, plan, options with { SkipMissing = skipMissing });
    }

    public static DownloadJob CreateQuietly(Session session, DownloadRequest request)
    {
        var root = Path.GetFullPath(request.InstallRoot);
        Directory.CreateDirectory(root);

        using var resume = ResumeLog.Open(InstallRecord.StateDirectory(root), request.IsAddon ? ResumeLog.AddonLog : ResumeLog.BaseLog);
        var plan = BuildPlan(request, request.Source.Manifest, root, resume, announce: false);
        var options = session.BuildDownloadOptions(root, request.Workers, request.CacheBudgetMiB);
        return new DownloadJob(request, root, plan, options with { SkipMissing = request.SkipMissing });
    }

    private static bool ConfirmFolder(DownloadRequest request, BuildManifest manifest, string root)
    {
        if (request.IsAddon || request.RepairFiles is not null) return true;

        var existing = InstallRecord.TryLoad(root);
        if (existing is null || existing.BuildVersion.Length == 0) return true;
        if (existing.BuildVersion.Equals(manifest.BuildVersion, StringComparison.Ordinal)) return true;

        Output.Blank();
        Output.Warn($"This folder already holds {Naming.ShortenBuildVersion(existing.BuildVersion)}.");
        Output.Detail($"Downloading {manifest.ShortVersion} into it overwrites shared files and leaves a mix of both.");
        Output.Blank();
        return Prompt.Ask("Download into this folder anyway?", false) == true;
    }

    private static bool AnnounceQueue(Session session)
    {
        var mine = session.Downloads.Snapshot().Count(job => job.IsActive);
        var others = DownloadQueue.Others(session.Paths);
        if (mine == 0 && others.Count == 0) return false;

        Output.Blank();

        if (mine > 0)
            Output.Info(mine == 1
                ? "Something is already downloading, this joins the queue behind it."
                : $"{mine} downloads are already lined up, this joins the queue behind them.");
        else
            Output.Info(others[0].Ticket is { } running
                ? $"Another window is downloading {running.Build}, this joins the queue behind it."
                : "Another window is downloading, this joins the queue behind it.");

        return true;
    }

    public static async Task<AvailabilityReport> ProbeAsync(
        Session session,
        DownloadPlan plan,
        CancellationToken cancellation)
    {
        Output.Blank();

        AvailabilityReport report;
        using (Spinner.Start("Checking what Epic still hosts for this build"))
        {
            report = await AvailabilityProbe
                .RunAsync(plan, session.Http, 48, cancellation)
                .ConfigureAwait(false);
        }

        if (report.Sampled == 0) return report;

        if (report.IsOffline)
        {
            Output.Error("Could not reach any of Epic's CDN mirrors, so nothing can be downloaded right now.");
            Output.Hint("Check your connection, a VPN or firewall can also block them.");
            return report;
        }

        if (report.Unreachable > 0)
            Output.Detail($"{report.Unreachable} of the {report.Sampled} sampled chunks got no answer and were left out.");

        if (report.IsComplete)
        {
            Output.Success($"All {report.Checked} sampled chunks are still on the CDN.");
            return report;
        }

        Output.Warn($"Only {Format.Percent(report.Fraction)} of a {report.Checked} chunk sample is still hosted.");
        Output.Detail("Epic removes chunks for old builds, so part of this build can no longer be downloaded.");
        return report;
    }

    public static DownloadPlan BuildPlan(
        DownloadRequest request,
        BuildManifest manifest,
        string root,
        ResumeLog resume,
        bool announce)
    {
        if (request.RepairFiles is not null)
            return DownloadPlanner.CreateForFiles(manifest, request.RepairFiles, request.Tags ?? []);

        var scan = request.Fresh ? default : resume.Scan(manifest, root);

        var done = scan.Completed;

        if (!request.Fresh && InstallRecord.TryLoad(root) is { HasUnavailable: true } existing)
        {
            var skipped = existing.UnavailableSet();
            if (done is not null) skipped.UnionWith(done);
            done = skipped;
        }

        if (announce && scan.HasProgress)
        {
            Output.Info($"Resuming: {Format.Count(scan.Completed.Count)} files already downloaded.");
            if (scan.Missing > 0) Output.Detail($"{scan.Missing} recorded files are gone and will be fetched again.");
            if (scan.Changed > 0) Output.Detail($"{scan.Changed} files changed on disk and will be fetched again.");
        }

        var partial = request.Fresh ? null : resume.LoadPartial(manifest, root);

        return DownloadPlanner.Create(manifest, request.Tags, done, partial);
    }

    private static void Summarise(DownloadRequest request, DownloadPlan plan, string root, DownloadOptions options)
    {
        var manifest = plan.Manifest;

        Output.Section(request.Action);

        var panel = new Panel()
            .Row("Build", manifest.ShortVersion, Theme.Cyan)
            .Row("Manifest", $"{request.Source.ManifestId}  ({request.Source.Origin})")
            .Row("Format", $"{manifest.Format.ToString().ToLowerInvariant()}, feature level {manifest.FeatureLevel}")
            .Row("Folder", root);

        if (request.RepairFiles is null && !request.IsAddon)
            panel.Row("Install tags", DescribeTags(request.Tags, manifest));

        panel.Gap()
            .Row("Files to write", Format.Count(plan.Files.Count) +
                                   (plan.AlreadyComplete > 0
                                       ? $"  ({Format.Count(plan.AlreadyComplete)} already done)"
                                       : string.Empty))
            .Row("Chunks", Format.Count(plan.ChunkCount))
            .Row("Download size", Format.Bytes(plan.DownloadSize))
            .Row("Size on disk", Format.Bytes(plan.SelectedInstallSize))
            .Row("Memory budget", $"{Format.Bytes(DownloadEngine.ResolveMemoryBound(plan, options))}" +
                                  $"  ({Format.Bytes(plan.PeakCacheBytes)} held for chunk reuse)")
            .Row("Workers", options.Workers.ToString())
            .Render();
    }

    private static string DescribeTags(IReadOnlyList<string>? tags, BuildManifest manifest)
    {
        if (tags is null) return "everything";

        var available = manifest.InstallTags.Count + (manifest.HasUntaggedFiles ? 1 : 0);
        if (tags.Count == 0) return "nothing selected";
        if (tags.Count >= available) return $"everything ({tags.Count} tags)";

        var names = tags.Select(tag => tag.Length == 0 ? "(untagged)" : tag);
        return $"{tags.Count} of {available}: {string.Join(", ", names)}";
    }

    internal static int? KnownMissing(DownloadRequest request, BuildManifest manifest)
    {
        var groups = InstallTagCatalog.Summarise(manifest);
        var chosen = new HashSet<string>(request.Tags ?? TagPresets.Intersect(TagPresets.Full.Resolve(groups), manifest),
            StringComparer.Ordinal);

        var preset = TagPresets.All.FirstOrDefault(preset =>
            chosen.SetEquals(TagPresets.Intersect(preset.Resolve(groups), manifest)));

        return preset is null
            ? null
            : VerifiedBuilds.MissingFor(request.Source.Release?.Version ?? manifest.ShortVersion, preset.Key);
    }

    internal static bool HasRoomOnDisk(string root, DownloadPlan plan, out long free)
    {
        free = long.MaxValue;

        try
        {
            var drive = Path.GetPathRoot(Path.GetFullPath(root));
            if (string.IsNullOrEmpty(drive)) return true;
            free = new DriveInfo(drive).AvailableFreeSpace;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return true;
        }

        return free >= plan.WriteSize;
    }

    public static void SaveRecord(DownloadRequest request, string root, DownloadPlan plan, bool complete,
        IReadOnlyList<FailedFile>? failures = null)
    {
        var manifest = plan.Manifest;
        var record = InstallRecord.TryLoad(root) ?? new InstallRecord();

        if (request.IsAddon)
        {
            record.UefnManifestId = request.Source.ManifestId;
            if (complete) record.UefnInstalledUtc = DateTime.UtcNow;
            record.Save(root);
            CopyManifest(request, root);
            return;
        }

        record.BuildVersion = manifest.BuildVersion;
        record.ManifestId = request.Source.ManifestId;
        record.AppName = manifest.AppName;
        record.LaunchExe = manifest.LaunchExe;
        record.InstallSize = plan.SelectedInstallSize;
        if (request.RepairFiles is null) record.InstallTags = [.. request.Tags ?? []];
        if (failures is not null)
        {
            var missing = failures.Where(failure => failure.NotHosted).Select(failure => failure.FileName).ToHashSet(StringComparer.Ordinal);
            var written = plan.Files.Select(file => file.FileName).Where(name => !missing.Contains(name)).ToHashSet(StringComparer.Ordinal);
            record.Unavailable = [.. record.Unavailable.Where(name => !written.Contains(name)).Union(missing, StringComparer.Ordinal)];
        }

        if (complete) record.CompletedUtc = DateTime.UtcNow;
        else if (request.RepairFiles is null && record.CompletedUtc is not null)
        {
            record.CompletedUtc = null;
            record.Extended = true;
        }

        record.Save(root);

        CopyManifest(request, root);
    }

    private static void CopyManifest(DownloadRequest request, string root) =>
        CopyManifest(request.Source, request.IsAddon
            ? InstallRecord.UefnManifestPath(root)
            : InstallRecord.ManifestPath(root));

    internal static void CopyManifest(ResolvedManifest source, string target)
    {
        try
        {
            if (File.Exists(target)) return;

            var cached = Path.Combine(AppPaths.Create().Manifests, source.ManifestId + ".manifest");

            if (File.Exists(cached)) File.Copy(cached, target, overwrite: true);
            else if (File.Exists(source.Origin)) File.Copy(source.Origin, target, overwrite: true);
        }
        catch (IOException)
        {
        }
    }

    public static void Report(DownloadResult result, string root)
    {
        Output.Blank();

        if (result.Files == 0 && result.Failures.Count > 0)
            Output.Error("Nothing could be assembled, every selected file needs chunks Epic has removed.");
        else if (result.Failures.Count > 0)
            Output.Warn($"Finished in {Format.Duration(result.Elapsed)}, but {Format.Count(result.Failures.Count)} files are incomplete.");
        else
            Output.Success($"Finished in {Format.Duration(result.Elapsed)}.");

        Output.Blank();

        var panel = new Panel()
            .Row("Files written", Format.Count(result.Files))
            .Row("Chunks fetched", Format.Count(result.Chunks))
            .Row("Transferred", Format.Bytes(result.TransferredBytes))
            .Row("Written", Format.Bytes(result.WrittenBytes));

        if (result.Elapsed.TotalSeconds > 1)
            panel.Row("Average speed", Format.Rate(result.TransferredBytes / result.Elapsed.TotalSeconds));

        panel.Render();

        if (result.Failures.Count > 0)
        {
            Output.Section("Could not be downloaded");

            foreach (var failure in result.Failures.Take(15))
                Output.Bullet(Format.TruncateStart(failure.FileName, ConsoleEx.Width - 10));

            if (result.Failures.Count > 15)
                Output.Detail($"and {Format.Count(result.Failures.Count - 15)} more");

            Output.Blank();
            Output.Hint("Epic no longer hosts the chunks these files are built from.");
        }

        Output.Blank();
        Output.Hint(result.Files == 0 ? $"Folder: {root}" : $"Installed to {root}");
    }
}
