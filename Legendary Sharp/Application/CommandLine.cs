using System.Text.Json;
using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal static class CommandLine
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int UsageError = 2;
    public const int Incomplete = 3;
    public const int Cancelled = 130;

    private static readonly TimeSpan LineInterval = TimeSpan.FromSeconds(10);

    private static Stream? _json;

    public static async Task<int> RunAsync(string[] args)
    {
        CommandArgs parsed;

        try
        {
            parsed = CommandArgs.Parse(args);
        }
        catch (CommandException error)
        {
            return Fail(error);
        }

        if (parsed.Json)
        {
            _json = Console.OpenStandardOutput();
            Console.SetOut(Console.Error);
        }

        if (parsed.Command == "version") return Version(parsed);
        if (parsed.Command == "help" || parsed.Has("help")) return Help();

        var cancellation = Interrupt.Begin();

        try
        {
            using var session = Session.Create();

            return parsed.Command switch
            {
                "list" => await ListAsync(session, parsed, cancellation).ConfigureAwait(false),
                "info" => await InfoAsync(session, parsed, cancellation).ConfigureAwait(false),
                "download" => await DownloadAsync(session, parsed, cancellation).ConfigureAwait(false),
                "builds" => Builds(session, parsed),
                "verify" => await VerifyAsync(session, parsed, cancellation).ConfigureAwait(false),
                "repair" => await RepairAsync(session, parsed, cancellation).ConfigureAwait(false),
                _ => throw new CommandException($"Unknown command \"{parsed.Command}\".")
            };
        }
        catch (CommandException error)
        {
            return Fail(error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Output.Warn("Stopped.");
            return Cancelled;
        }
        catch (Exception error)
        {
            Output.Error(error.Message);
            if (ErrorLog.Write(error, "command " + parsed.Command) is { } log) Output.Hint($"Details were saved to {log}");
            return Failure;
        }
    }

    private static int Fail(CommandException error)
    {
        Output.Error(error.Message);
        if (error.ExitCode == UsageError) Output.Hint($"Run {AppInfo.Command} --help to see every command.");
        return error.ExitCode;
    }

    private static int Version(CommandArgs args)
    {
        if (args.Json) WriteJson(writer => writer.WriteString("version", AppInfo.Version));
        else Output.Line($"{AppInfo.Name} {AppInfo.Version}");
        return Success;
    }

    private static int Help()
    {
        var c = AppInfo.Command;

        Output.Line($"{AppInfo.Name} {AppInfo.Version}");
        Output.Blank();
        Output.Line($"Run {c} on its own for the menu. Give it a command to use it from scripts and AI agents.");
        Output.Blank();
        Output.Section("Commands");
        Output.Pair("list [text]", "builds that can be downloaded, filtered by text", 24);
        Output.Pair("info <build>", "sizes, presets and how much Epic still hosts", 24);
        Output.Pair("download <build>", "download a build and wait until it finishes", 24);
        Output.Pair("builds", "builds you have installed", 24);
        Output.Pair("verify <folder>", "hash every file of an installed build", 24);
        Output.Pair("repair <folder>", "verify, then download anything broken", 24);
        Output.Section("Options");
        Output.Pair("--json", "print the result as JSON, progress goes to stderr", 24);
        Output.Pair("--season <n>", "list: only that season", 24);
        Output.Pair("--confirmed", "list: only builds that download in full", 24);
        Output.Pair("--all", "list: include Android, iOS and Mac builds", 24);
        Output.Pair("--preset <key>", $"download: {string.Join(", ", TagPresets.All.Select(preset => preset.Key))}", 24);
        Output.Pair("--tags <a,b>", "download: exact install tags instead of a preset", 24);
        Output.Pair("--folder <path>", "download: where to put it, defaults to your install root", 24);
        Output.Pair("--skip-missing", "download, repair: take what Epic still hosts", 24);
        Output.Pair("--yes", "download: use a folder that holds another build", 24);
        Output.Section("Examples");
        Output.Line($"{c} list --confirmed");
        Output.Line($"{c} download 23.50-CL-24441668 --preset playable --folder D:\\Builds\\23.50");
        Output.Line($"{c} verify D:\\Builds\\23.50 --json");
        Output.Section("Exit codes");
        Output.Line("0 done, 1 failed, 2 wrong usage, 3 finished with files missing or broken, 130 stopped with Ctrl+C");
        return Success;
    }

    private static async Task<int> ListAsync(Session session, CommandArgs args, CancellationToken cancellation)
    {
        var catalog = await session.CatalogAsync(false, cancellation).ConfigureAwait(false);
        var season = args.Value("season");

        if (season is not null && catalog.ResolveSeason(season) is null)
            throw new CommandException($"There is no season \"{season}\".", Failure);

        var entries = catalog.Search(args.Argument, season, onlyDownloadable: true)
            .Where(entry => !VerifiedBuilds.IsUnavailable(entry.Version))
            .Where(entry => args.Has("all") || !VerifiedBuilds.IsOtherPlatform(entry.Version))
            .Where(entry => !args.Has("confirmed") || entry.IsVerified)
            .ToList();

        if (args.Json)
        {
            WriteJsonArray(entries, WriteRelease);
            return Success;
        }

        if (entries.Count == 0)
        {
            Output.Info("No builds match.");
            return Success;
        }

        var width = entries.Max(entry => entry.Version.Length);
        foreach (var entry in entries) Output.Line($"{entry.Version.PadRight(width)}   {Status(entry)}");

        Output.Blank();
        Output.Hint($"{Format.Count(entries.Count)} builds. {AppInfo.Command} info <build> shows one in detail.");
        return Success;
    }

    private static async Task<int> InfoAsync(Session session, CommandArgs args, CancellationToken cancellation)
    {
        var resolved = await ResolveAsync(session, args, "info", cancellation).ConfigureAwait(false);
        var manifest = resolved.Manifest;
        var version = resolved.Release?.Version ?? manifest.ShortVersion;
        var groups = InstallTagCatalog.Summarise(manifest);

        var presets = TagPresets.All
            .Select(preset => (Preset: preset, Size: InstallTagCatalog.SizeOf(manifest,
                TagPresets.Intersect(preset.Resolve(groups), manifest)), Missing: VerifiedBuilds.MissingFor(version, preset.Key)))
            .ToList();

        if (args.Json)
        {
            WriteJson(writer =>
            {
                writer.WriteString("version", version);
                writer.WriteString("buildVersion", manifest.BuildVersion);
                writer.WriteString("manifestId", resolved.ManifestId);
                writer.WriteBoolean("confirmed", VerifiedBuilds.IsVerified(version));
                writer.WriteNumber("files", manifest.Files.Count);
                writer.WriteNumber("chunks", manifest.Chunks.Count);
                writer.WriteNumber("installSize", manifest.InstallSize);

                writer.WriteStartArray("presets");
                foreach (var (preset, size, missing) in presets)
                {
                    writer.WriteStartObject();
                    writer.WriteString("key", preset.Key);
                    writer.WriteString("name", preset.Name);
                    writer.WriteNumber("size", size);
                    WriteMissing(writer, missing);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteStartArray("tags");
                foreach (var group in groups)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", group.Display);
                    writer.WriteStartArray("tags");
                    foreach (var tag in group.Tags) writer.WriteStringValue(tag);
                    writer.WriteEndArray();
                    writer.WriteNumber("size", group.InstallSize);
                    writer.WriteString("description", group.Description);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            });

            return Success;
        }

        Output.Pair("Build", version);
        Output.Pair("Manifest", resolved.ManifestId);
        Output.Pair("Files", Format.Count(manifest.Files.Count));
        Output.Pair("Full size", Format.Bytes(manifest.InstallSize));
        Output.Pair("Status", VerifiedBuilds.IsVerified(version)
            ? "confirmed, downloads in full"
            : VerifiedBuilds.MissingNote(version) ?? "not checked yet");

        Output.Section("Presets");
        foreach (var (preset, size, missing) in presets)
            Output.Pair(preset.Key, $"{Format.Bytes(size),10}   {preset.Name}" +
                                    (missing is > 0 ? $", {Format.Files(missing.Value)} missing" : string.Empty), 14);

        if (groups.Count > 1)
        {
            Output.Section("Install tags");
            var width = groups.Max(group => group.Display.Length) + 2;
            foreach (var group in groups)
                Output.Pair(group.Display, $"{Format.Bytes(group.InstallSize),10}   {group.Description}", width);
        }

        return Success;
    }

    private static async Task<int> DownloadAsync(Session session, CommandArgs args, CancellationToken cancellation)
    {
        var resolved = await ResolveAsync(session, args, "download", cancellation).ConfigureAwait(false);
        var manifest = resolved.Manifest;
        var version = resolved.Release?.Version ?? manifest.ShortVersion;
        var tags = PickTags(session, args, manifest, version);

        var folder = Path.GetFullPath(args.Value("folder") ?? Path.Combine(session.Settings.EffectiveInstallRoot,
            Naming.SanitiseFolder(resolved.Release?.SuggestedFolderName is { Length: > 0 } name ? name : manifest.ShortVersion)));

        if (session.Downloads.Holds(folder) || DownloadQueue.Busy(session.Paths, folder) is not null)
            throw new CommandException("Another window is downloading into that folder right now.", Failure);

        if (InstallRecord.TryLoad(folder) is { BuildVersion.Length: > 0 } existing &&
            !existing.BuildVersion.Equals(manifest.BuildVersion, StringComparison.Ordinal) && !args.Has("yes"))
            throw new CommandException(
                $"{folder} already holds {Naming.ShortenBuildVersion(existing.BuildVersion)}. " +
                "Pick another --folder, or add --yes to download into it anyway.", Failure);

        var request = new DownloadRequest
        {
            Source = resolved,
            InstallRoot = folder,
            Tags = tags,
            AssumeYes = true,
            SkipMissing = args.Has("skip-missing")
        };

        var job = DownloadWorkflow.CreateQuietly(session, request);

        if (job.Plan.IsEmpty)
        {
            DownloadWorkflow.SaveRecord(request, folder, job.Plan, complete: true);
            Output.Success($"{version} is already complete in {folder}.");
            if (args.Json) WriteResult(job.View() with { State = JobState.Done });
            return Success;
        }

        if (!DownloadWorkflow.HasRoomOnDisk(folder, job.Plan, out var free))
            throw new CommandException(
                $"Not enough free space: {Format.Bytes(job.Plan.WriteSize)} needed, {Format.Bytes(free)} available.", Failure);

        Output.Info($"Downloading {version} into {folder}");
        Output.Detail($"{Format.Bytes(job.Plan.DownloadSize)} to download, {Format.Files(job.Plan.Files.Count)} to write");

        if (VerifiedBuilds.IsKnownPatchy(version))
            Output.Warn(DownloadWorkflow.KnownMissing(request, manifest) is { } files
                ? $"Epic no longer hosts all of this build, {Format.Files(files)} will be missing with these tags."
                : "Epic no longer hosts all of this build, expect missing files.");

        return await RunJobAsync(session, job, args, cancellation).ConfigureAwait(false);
    }

    private static int Builds(Session session, CommandArgs args)
    {
        var builds = BuildLibrary.Scan(session.Settings.ScanRoots());

        if (args.Json)
        {
            WriteJsonArray(builds, (writer, build) =>
            {
                writer.WriteString("version", Naming.ShortenBuildVersion(build.Record.BuildVersion));
                writer.WriteString("buildVersion", build.Record.BuildVersion);
                writer.WriteString("folder", build.Path);
                writer.WriteNumber("size", build.Record.InstallSize);
                writer.WriteBoolean("complete", build.Record.IsComplete);
                writer.WriteBoolean("uefn", build.Record.HasUefn);
            });

            return Success;
        }

        if (builds.Count == 0)
        {
            Output.Info("No builds found yet.");
            Output.Hint($"Looked in {string.Join(", ", session.Settings.ScanRoots())}");
            return Success;
        }

        var versions = builds.Select(build => Naming.ShortenBuildVersion(build.Record.BuildVersion)).ToList();
        var width = versions.Max(version => version.Length);

        for (var index = 0; index < builds.Count; index++)
            Output.Line($"{versions[index].PadRight(width)}   {Format.Bytes(builds[index].Record.InstallSize),10}   " +
                        $"{builds[index].Status,-10}   {builds[index].Path}");

        return Success;
    }

    private static async Task<int> VerifyAsync(Session session, CommandArgs args, CancellationToken cancellation)
    {
        var build = await OpenAsync(session, args, "verify", cancellation).ConfigureAwait(false);
        var report = await CheckAsync(build, cancellation).ConfigureAwait(false);

        if (args.Json)
        {
            WriteJson(writer =>
            {
                writer.WriteString("folder", build.Root);
                writer.WriteBoolean("healthy", report.IsHealthy);
                writer.WriteNumber("files", report.Healthy + report.Broken);
                WriteNames(writer, "missing", report.Missing);
                WriteNames(writer, "damaged", report.Damaged);
            });
        }

        return report.IsHealthy ? Success : Incomplete;
    }

    private static async Task<int> RepairAsync(Session session, CommandArgs args, CancellationToken cancellation)
    {
        var build = await OpenAsync(session, args, "repair", cancellation).ConfigureAwait(false);
        var report = await CheckAsync(build, cancellation).ConfigureAwait(false);

        if (report.IsHealthy)
        {
            if (args.Json) WriteJson(writer => writer.WriteString("status", "healthy"));
            return Success;
        }

        var job = DownloadWorkflow.CreateQuietly(session, new DownloadRequest
        {
            Source = build.Source,
            InstallRoot = build.Root,
            Tags = build.Record.TagFilter,
            RepairFiles = report.NeedsRepair,
            AssumeYes = true,
            SkipMissing = args.Has("skip-missing")
        });

        Output.Info($"Repairing {Format.Files(report.Broken)}");
        return await RunJobAsync(session, job, args, cancellation).ConfigureAwait(false);
    }

    private static async Task<ResolvedManifest> ResolveAsync(
        Session session,
        CommandArgs args,
        string command,
        CancellationToken cancellation)
    {
        var reference = args.Argument ??
                        throw new CommandException($"Which build? For example: {AppInfo.Command} {command} 23.50-CL-24441668");

        var catalog = await session.CatalogAsync(false, cancellation).ConfigureAwait(false);

        if (!File.Exists(reference) && catalog.Find(reference) is null)
        {
            var matches = catalog.Downloadable.Where(entry => entry.Matches(reference)).Select(entry => entry.Version).ToList();

            if (matches.Count > 1)
                throw new CommandException(
                    $"\"{reference}\" matches {matches.Count} builds: {string.Join(", ", matches.Take(6))}" +
                    $"{(matches.Count > 6 ? ", ..." : string.Empty)}. Use the full version.", Failure);
        }

        return await ManifestResolver.ResolveAsync(reference, session.Http, session.Paths, catalog, cancellation)
            .ConfigureAwait(false);
    }

    private static async Task<OpenedBuild> OpenAsync(Session session, CommandArgs args, string command, CancellationToken cancellation)
    {
        var folder = args.Argument ??
                     throw new CommandException($"Which folder? For example: {AppInfo.Command} {command} D:\\Builds\\23.50");

        if (DownloadQueue.Busy(session.Paths, folder) is { } ticket)
            throw new CommandException($"Another window is working on that folder right now ({ticket.Action.ToLowerInvariant()} of {ticket.Build}).", Failure);

        return await InstalledBuildLoader.OpenAsync(session, folder, cancellation).ConfigureAwait(false);
    }

    private static async Task<VerifyReport> CheckAsync(OpenedBuild build, CancellationToken cancellation)
    {
        var files = InstalledBuildLoader.SelectedFiles(build);
        var last = DateTime.UtcNow;

        Output.Info($"Verifying {Naming.ShortenBuildVersion(build.Record.BuildVersion)}, {Format.Files(files.Count)}");

        var report = await InstallVerifier.VerifyAsync(files, build.Root, progress =>
        {
            if (DateTime.UtcNow - last < LineInterval) return;
            last = DateTime.UtcNow;
            Output.Detail($"{Format.Percent(progress.BytesTotal == 0 ? 1d : (double)progress.BytesDone / progress.BytesTotal)}  " +
                          $"{Format.Count(progress.FilesDone)} of {Format.Count(progress.FilesTotal)} files");
        }, cancellation).ConfigureAwait(false);

        if (report.IsHealthy) Output.Success($"All {Format.Files(report.Healthy)} are healthy.");
        else Output.Warn($"{Format.Count(report.Missing.Count)} missing, {Format.Count(report.Damaged.Count)} damaged.");

        return report;
    }

    private static List<string> PickTags(Session session, CommandArgs args, BuildManifest manifest, string version)
    {
        var groups = InstallTagCatalog.Summarise(manifest);
        if (groups.Count <= 1) return InstallTagCatalog.Flatten(groups);

        if (args.Value("tags") is { } list)
        {
            var wanted = list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            var usable = TagPresets.Intersect(wanted, manifest);

            if (usable.Count == 0)
                throw new CommandException(
                    $"None of those tags exist in this build. {AppInfo.Command} info {version} lists them.", Failure);

            var required = groups.Where(group => group.Kind is TagKind.Untagged or TagKind.Core).SelectMany(group => group.Tags);
            return [.. required.Union(usable, StringComparer.Ordinal)];
        }

        var key = args.Value("preset") ?? session.Settings.Preset;
        var preset = TagPresets.Find(key) ?? throw new CommandException(
            $"There is no preset \"{key}\". Use one of {string.Join(", ", TagPresets.All.Select(item => item.Key))}.");

        return TagPresets.Intersect(preset.Resolve(groups), manifest);
    }

    private static async Task<int> RunJobAsync(Session session, DownloadJob job, CommandArgs args, CancellationToken cancellation)
    {
        var live = ConsoleEx.IsInteractive && ConsoleEx.SupportsColor && !args.Json;
        var last = DateTime.MinValue;

        session.Downloads.Enqueue(job);

        try
        {
            while (true)
            {
                await Task.Delay(live ? 500 : 1000, cancellation).ConfigureAwait(false);

                var view = session.Downloads.Snapshot().First(item => item.Id == job.Id);

                if (view.State == JobState.NeedsAttention)
                {
                    EndLive(live);
                    await session.Downloads.CancelAsync(job.Id).ConfigureAwait(false);
                    throw new CommandException(
                        "Part of this build is missing from Epic's CDN. Run again with --skip-missing to take the rest.",
                        Incomplete);
                }

                if (view.IsFinished)
                {
                    EndLive(live);
                    return Finish(view, args);
                }

                if (!live && DateTime.UtcNow - last < LineInterval) continue;
                last = DateTime.UtcNow;

                if (live) ConsoleEx.Write("\r" + Output.Indent + Describe(view) + Ansi.EraseToLineEnd);
                else Output.Detail(Describe(view));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            EndLive(live);
            await session.Downloads.CancelAsync(job.Id).ConfigureAwait(false);

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline && !session.Downloads.Snapshot().First(item => item.Id == job.Id).IsFinished)
                await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);

            Output.Warn("Stopped. What is downloaded is kept, run the same command again to carry on.");
            return Cancelled;
        }
    }

    private static void EndLive(bool live)
    {
        if (live) ConsoleEx.WriteLine();
    }

    private static string Describe(JobView view)
    {
        if (view.Blocker is { } blocker) return $"waiting for another window to finish {blocker.Build}";
        if (view.Progress is not { } progress) return "starting";

        return $"{Format.Percent(view.Fraction),6}   {Format.Bytes(progress.DownloadedBytes)} of " +
               $"{Format.Bytes(progress.TotalDownloadBytes)}   {Format.Rate(progress.BytesPerSecond)}   " +
               $"{Format.Duration(progress.Eta)} left";
    }

    private static int Finish(JobView view, CommandArgs args)
    {
        var missing = view.Result?.Failures.Count ?? 0;

        switch (view.State)
        {
            case JobState.Done when missing == 0:
                Output.Success(view.Message.Length > 0 ? view.Message : "Finished.");
                break;
            case JobState.Done:
                Output.Warn($"{Format.Files(missing)} could not be downloaded, Epic no longer hosts them.");
                break;
            default:
                Output.Error(view.Message.Length > 0 ? view.Message : "The download stopped.");
                break;
        }

        if (args.Json) WriteResult(view);

        return view.State switch
        {
            JobState.Done => missing == 0 ? Success : Incomplete,
            JobState.Cancelled => Cancelled,
            _ => Failure
        };
    }

    private static void WriteResult(JobView view) => WriteJson(writer =>
    {
        writer.WriteString("status", view.State switch
        {
            JobState.Done => "done",
            JobState.Cancelled => "cancelled",
            _ => "failed"
        });
        writer.WriteString("build", view.Title);
        writer.WriteString("folder", view.Root);
        writer.WriteString("message", view.Message);

        if (view.Result is { } result)
        {
            writer.WriteNumber("files", result.Files);
            writer.WriteNumber("downloadedBytes", result.TransferredBytes);
            writer.WriteNumber("seconds", Math.Round(result.Elapsed.TotalSeconds, 1));
            writer.WriteStartArray("missing");
            foreach (var failure in result.Failures) writer.WriteStringValue(failure.FileName);
            writer.WriteEndArray();
        }
    });

    private static string Status(ReleaseEntry entry)
    {
        if (entry.IsVerified) return "confirmed complete";
        return VerifiedBuilds.MissingNote(entry.Version) ?? (entry.IsKnownPatchy ? "expect missing files" : string.Empty);
    }

    private static void WriteRelease(Utf8JsonWriter writer, ReleaseEntry entry)
    {
        writer.WriteString("version", entry.Version);
        writer.WriteString("season", entry.Season);
        writer.WriteString("manifestId", entry.ManifestId);
        writer.WriteBoolean("confirmed", entry.IsVerified);

        writer.WriteStartObject("missingFiles");
        foreach (var preset in TagPresets.All)
        {
            if (VerifiedBuilds.MissingFor(entry.Version, preset.Key) is { } count) writer.WriteNumber(preset.Key, count);
            else writer.WriteNull(preset.Key);
        }
        writer.WriteEndObject();
    }

    private static void WriteMissing(Utf8JsonWriter writer, int? missing)
    {
        if (missing is { } count) writer.WriteNumber("missingFiles", count);
        else writer.WriteNull("missingFiles");
    }

    private static void WriteNames(Utf8JsonWriter writer, string name, IEnumerable<FileEntry> files)
    {
        writer.WriteStartArray(name);
        foreach (var file in files) writer.WriteStringValue(file.FileName);
        writer.WriteEndArray();
    }

    private static void WriteJsonArray<T>(IEnumerable<T> items, Action<Utf8JsonWriter, T> write) =>
        Emit(writer =>
        {
            writer.WriteStartArray();
            foreach (var item in items)
            {
                writer.WriteStartObject();
                write(writer, item);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        });

    private static void WriteJson(Action<Utf8JsonWriter> write) =>
        Emit(writer =>
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        });

    private static void Emit(Action<Utf8JsonWriter> write)
    {
        var stream = _json ?? Console.OpenStandardOutput();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            write(writer);

        stream.Write("\n"u8);
        stream.Flush();
    }
}
