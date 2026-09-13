using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed record DownloadRequest
{
    public required ResolvedManifest Source { get; init; }

    public required string InstallRoot { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

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
    public static async Task<int> RunAsync(Session session, DownloadRequest request, CancellationToken cancellation)
    {
        var manifest = request.Source.Manifest;
        var root = Path.GetFullPath(request.InstallRoot);
        Directory.CreateDirectory(root);

        using var resume = ResumeLog.Open(InstallRecord.StateDirectory(root),
            request.IsAddon ? "uefn.txt" : "completed.txt");
        if (request.Fresh) resume.Clear();

        var plan = BuildPlan(request, manifest, root, resume);
        var options = session.BuildDownloadOptions(root, request.Workers, request.CacheBudgetMiB);

        Summarise(request, plan, root, options);

        if (plan.IsEmpty)
        {
            Output.Blank();
            Output.Success("Nothing to do, every selected file is already in place.");
            SaveRecord(request, root, plan, complete: true);
            return 0;
        }

        if (!HasRoomOnDisk(root, plan, out var free))
        {
            Output.Blank();
            Output.Error($"Not enough free space: {Format.Bytes(plan.WriteSize)} needed, {Format.Bytes(free)} available.");
            return 1;
        }

        var skipMissing = request.SkipMissing;

        if (!request.IsAddon && VerifiedBuilds.IsKnownPatchy(manifest.ShortVersion))
        {
            Output.Blank();
            Output.Warn("Builds this old are usually missing chunks whatever tags you pick.");
            Output.Detail("Nothing below 13.40 is known to download in full.");
        }

        var availability = await ProbeAsync(session, plan, cancellation).ConfigureAwait(false);

        if (!availability.IsComplete)
        {
            if (availability.IsHopeless)
            {
                Output.Blank();
                Output.Error("None of the sampled chunks are on Epic's CDN any more, this build cannot be downloaded.");
                return 1;
            }

            if (!skipMissing)
            {
                if (!ConsoleEx.IsInteractive || request.AssumeYes)
                {
                    Output.Hint("Add --skip-missing to download what is still there.");
                }
                else
                {
                    Output.Blank();
                    skipMissing = Prompt.Ask("Download what is still available and list the rest?") == true;
                    if (!skipMissing) return 130;
                }
            }
        }

        if (!request.AssumeYes && ConsoleEx.IsInteractive)
        {
            Output.Blank();
            if (Prompt.Ask($"Start the {request.Action.ToLowerInvariant()}?") != true)
            {
                Output.Hint("Cancelled.");
                return 130;
            }
        }

        SaveRecord(request, root, plan, complete: false);

        var engine = new DownloadEngine(options with { SkipMissing = skipMissing });

        Output.Blank();
        using var display = new ProgressDisplay(request.Action, manifest.ShortVersion);

        DownloadResult result;

        try
        {
            result = await engine
                .RunAsync(plan, resume, progress => display.Update(progress), cancellation)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            display.Finish();
            Output.Blank();
            Output.Warn("Stopped. Run the same command again to pick up where it left off.");
            return 130;
        }
        catch (Exception error)
        {
            display.Finish();
            Output.Blank();
            Output.Error(error.Message);
            Output.Hint("Progress is saved, run the same command again to resume.");
            return 1;
        }

        display.Finish();
        SaveRecord(request, root, plan, complete: result.Failures.Count == 0);
        Report(result, root);

        if (result.Failures.Count > 0) return 1;

        if (!request.IsAddon && request.RepairFiles is null)
            await UefnWorkflow.OfferAsync(session, root, manifest.BuildVersion, cancellation).ConfigureAwait(false);

        return 0;
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

        if (report.IsComplete)
        {
            Output.Success($"All {report.Sampled} sampled chunks are still on the CDN.");
            return report;
        }

        Output.Warn($"Only {Format.Percent(report.Fraction)} of a {report.Sampled} chunk sample is still hosted.");
        Output.Detail("Epic removes chunks for old builds, so part of this build can no longer be downloaded.");
        return report;
    }

    private static DownloadPlan BuildPlan(
        DownloadRequest request,
        BuildManifest manifest,
        string root,
        ResumeLog resume)
    {
        if (request.RepairFiles is not null)
            return DownloadPlanner.CreateForFiles(manifest, request.RepairFiles, request.Tags);

        var scan = request.Fresh ? default : resume.Scan(manifest, root);

        if (scan.HasProgress)
        {
            Output.Info($"Resuming: {Format.Count(scan.Completed.Count)} files already downloaded.");
            if (scan.Missing > 0) Output.Detail($"{scan.Missing} recorded files are gone and will be fetched again.");
            if (scan.Changed > 0) Output.Detail($"{scan.Changed} files changed on disk and will be fetched again.");
        }

        return DownloadPlanner.Create(manifest, request.Tags, scan.Completed);
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

        if (request.RepairFiles is null) panel.Row("Install tags", DescribeTags(request.Tags, manifest));

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

    private static string DescribeTags(IReadOnlyList<string> tags, BuildManifest manifest)
    {
        var available = manifest.InstallTags.Count + (manifest.HasUntaggedFiles ? 1 : 0);
        if (tags.Count == 0) return "everything";
        if (tags.Count >= available) return $"everything ({tags.Count} tags)";

        var names = tags.Select(tag => tag.Length == 0 ? "(untagged)" : tag);
        return $"{tags.Count} of {available}: {string.Join(", ", names)}";
    }

    private static bool HasRoomOnDisk(string root, DownloadPlan plan, out long free)
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

    private static void SaveRecord(DownloadRequest request, string root, DownloadPlan plan, bool complete)
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
        if (request.RepairFiles is null) record.InstallTags = [.. request.Tags];
        if (complete) record.CompletedUtc = DateTime.UtcNow;
        record.Save(root);

        CopyManifest(request, root);
    }

    private static void CopyManifest(DownloadRequest request, string root)
    {
        try
        {
            var target = request.IsAddon
                ? InstallRecord.UefnManifestPath(root)
                : InstallRecord.ManifestPath(root);

            if (File.Exists(target)) return;

            var cached = Path.Combine(
                AppPaths.Create().Manifests, request.Source.ManifestId + ".manifest");

            if (File.Exists(cached)) File.Copy(cached, target, overwrite: true);
            else if (File.Exists(request.Source.Origin)) File.Copy(request.Source.Origin, target, overwrite: true);
        }
        catch (IOException)
        {
        }
    }

    private static void Report(DownloadResult result, string root)
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
