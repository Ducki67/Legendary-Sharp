using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed class InteractiveShell(Session session)
{
    private const string VerifiedOnly = "confirmed";

    private enum MainAction
    {
        Download,
        Library,
        Browse,
        Settings,
        Quit
    }

    private enum BrowseAction
    {
        Download,
        Check,
        Info,
        Tags,
        Back
    }

    private enum BuildAction
    {
        Verify,
        Repair,
        Resume,
        Uefn,
        Reveal,
        Back
    }

    public async Task<int> RunAsync(CancellationToken cancellation)
    {
        while (true)
        {
            Header();

            var choices = new List<Choice<MainAction>>
            {
                new(MainAction.Download, "Download a build", "pick a version and pull it from the CDN"),
                new(MainAction.Library, "My builds", "verify, repair, resume or add UEFN to what is installed"),
                new(MainAction.Browse, "Browse releases", "every known build from polynite/fn-releases"),
                new(MainAction.Settings, "Settings", "install folder, workers, default preset"),
                new(MainAction.Quit, "Quit", string.Empty)
            };

            if (!SelectionList<MainAction>.TryPick("What would you like to do?", choices, out var action))
                return 0;

            try
            {
                switch (action)
                {
                    case MainAction.Download:
                        await DownloadAsync(cancellation).ConfigureAwait(false);
                        break;
                    case MainAction.Library:
                        await LibraryAsync(cancellation).ConfigureAwait(false);
                        break;
                    case MainAction.Browse:
                        await BrowseAsync(cancellation).ConfigureAwait(false);
                        break;
                    case MainAction.Settings:
                        Settings();
                        break;
                    case MainAction.Quit:
                        return 0;
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
                ConsoleEx.PauseForKey("Press any key to go back.");
            }
        }
    }

    private static void Header()
    {
        var lines = Logo.Lines(AppInfo.Tagline);
        lines.Add(string.Empty);
        ConsoleEx.Screen(lines);
    }

    private async Task DownloadAsync(CancellationToken cancellation)
    {
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

        var tags = PickTags(resolved.Manifest);
        if (tags is null) return;

        var folder = PickFolder(release, resolved);
        if (folder is null) return;

        var request = new DownloadRequest
        {
            Source = resolved,
            InstallRoot = folder,
            Tags = tags
        };

        await DownloadWorkflow.RunAsync(session, request, cancellation).ConfigureAwait(false);
        ConsoleEx.PauseForKey("Press any key to go back to the menu.");
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
        else if (entry.IsKnownPatchy) parts.Add("! expect missing files");

        if (entry.BuildDate.Length > 0) parts.Add(entry.BuildDate);
        if (entry.EngineVersion.Length > 0) parts.Add("UE " + entry.EngineVersion);
        if (entry.Notes.Length > 0) parts.Add(entry.Notes);
        return string.Join("   ", parts);
    }

    private static readonly TagPreset CustomTags = new("custom", "custom", "") { Includes = _ => false };

    private static readonly TagPreset FileTags = new("file", "file", "") { Includes = _ => false };

    private static List<string>? LoadTagFile(BuildManifest manifest)
    {
        Output.Blank();
        var path = Prompt.Path("Selective download file", mustExist: false);
        if (path is null) return null;

        try
        {
            var wanted = TagPresets.FromSelectiveDownloadFile(path);
            var usable = TagPresets.Intersect(wanted, manifest);

            Output.Blank();

            if (usable.Count == 0)
            {
                Output.Error("None of the tags in that file exist in this build.");
                Output.Hint("That file is probably for a different build era.");
                ConsoleEx.PauseForKey("Press any key to go back.");
                return null;
            }

            Output.Success($"{usable.Count} of {wanted.Count} tags from that file apply to this build.");
            Output.Pair("Size on disk", Format.Bytes(InstallTagCatalog.SizeOf(manifest, usable)));
            return usable;
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            Output.Blank();
            Output.Error(error.Message);
            ConsoleEx.PauseForKey("Press any key to go back.");
            return null;
        }
    }

    private static List<string>? PickTags(BuildManifest manifest)
    {
        var groups = InstallTagCatalog.Summarise(manifest);

        if (groups.Count <= 1)
        {
            Output.Blank();
            Output.Info($"This build has no install tags, the whole {Format.Bytes(manifest.InstallSize)} comes down.");
            return InstallTagCatalog.Flatten(groups);
        }

        var choices = TagPresets.All
            .Select(preset => new Choice<TagPreset?>(
                preset,
                $"{preset.Name}  ({Format.Bytes(InstallTagCatalog.SizeOf(manifest, preset.Resolve(groups)))})",
                preset.Description))
            .ToList();

        choices.Add(new Choice<TagPreset?>(CustomTags, "Pick tags myself", $"{groups.Count} groups available"));
        choices.Add(new Choice<TagPreset?>(FileTags, "Load from a file",
            "a selective download json, the kind shared for each build era"));

        if (!SelectionList<TagPreset?>.TryPick("How much of the build do you want?", choices, out var chosen))
            return null;

        if (ReferenceEquals(chosen, FileTags)) return LoadTagFile(manifest);
        if (!ReferenceEquals(chosen, CustomTags) && chosen is not null) return chosen.Resolve(groups);

        var required = groups
            .Select((group, index) => (group, index))
            .Where(item => item.group.Kind is TagKind.Untagged or TagKind.Core)
            .Select(item => item.index);

        var tagChoices = groups
            .Select(group => new Choice<TagGroup>(
                group,
                group.Display,
                Format.Bytes(group.InstallSize).PadLeft(10) + "   " + group.Description))
            .ToList();

        return MultiSelectList<TagGroup>.TryPick("Select install tags", tagChoices, required, out var picked,
            selected => Format.Bytes(InstallTagCatalog.SizeOf(manifest, InstallTagCatalog.Flatten(selected))) +
                        " on disk")
            ? InstallTagCatalog.Flatten(picked)
            : null;
    }

    private string? PickFolder(ReleaseEntry release, ResolvedManifest resolved)
    {
        var root = session.Settings.EffectiveInstallRoot;

        var suggestion = Path.Combine(root,
            Naming.SanitiseFolder(release.SuggestedFolderName.Length > 0
                ? release.SuggestedFolderName
                : resolved.Manifest.ShortVersion));

        Output.Blank();
        return Prompt.Path("Install folder", suggestion);
    }

    private async Task LibraryAsync(CancellationToken cancellation)
    {
        var builds = BuildLibrary.Scan(session.Settings.ScanRoots());

        if (builds.Count == 0)
        {
            Output.Blank();
            Output.Info("No builds found yet.");
            Output.Hint(session.Settings.InstallRoot.Length == 0
                ? "Set an install folder in Settings first."
                : $"Looked in {session.Settings.InstallRoot}");
            ConsoleEx.PauseForKey("Press any key to go back.");
            return;
        }

        var choices = builds
            .Select(build => new Choice<InstalledBuild>(
                build,
                Naming.ShortenBuildVersion(build.Record.BuildVersion),
                $"{Format.Bytes(build.Record.InstallSize)}  {build.Status}  {build.Path}"))
            .ToList();

        if (!SelectionList<InstalledBuild>.TryPick("Which build?", choices, out var chosen)) return;

        var actions = new List<Choice<BuildAction>>
        {
            new(BuildAction.Verify, "Verify", "hash every file against the manifest"),
            new(BuildAction.Repair, "Repair", "verify then re-download anything broken"),
            new(BuildAction.Resume, "Resume download", "continue an interrupted download",
                !chosen.Record.IsComplete),
            new(BuildAction.Uefn, "Install UEFN",
                chosen.Record.HasUefn ? "already installed, run again to restore files" : "add the Unreal Editor"),
            new(BuildAction.Reveal, "Show folder path", chosen.Path),
            new(BuildAction.Back, "Back", string.Empty)
        };

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

            case BuildAction.Verify:
            {
                var build = await InstalledBuildLoader.OpenAsync(session, chosen.Path, cancellation)
                    .ConfigureAwait(false);
                await VerifyWorkflow.RunAsync(build, cancellation).ConfigureAwait(false);
                break;
            }

            case BuildAction.Repair:
                await RepairWorkflow.RunAsync(session, chosen.Path, cancellation).ConfigureAwait(false);
                break;

            case BuildAction.Uefn:
                await UefnWorkflow.InstallForBuildAsync(session, chosen.Path, cancellation).ConfigureAwait(false);
                break;

            case BuildAction.Resume:
            {
                var build = await InstalledBuildLoader.OpenAsync(session, chosen.Path, cancellation)
                    .ConfigureAwait(false);

                await DownloadWorkflow.RunAsync(session, new DownloadRequest
                {
                    Source = build.Source,
                    InstallRoot = build.Root,
                    Tags = build.Record.InstallTags
                }, cancellation).ConfigureAwait(false);

                break;
            }
        }

        ConsoleEx.PauseForKey("Press any key to go back to the menu.");
    }

    private async Task BrowseAsync(CancellationToken cancellation)
    {
        var catalog = await Spinner
            .Run("Loading the release index", () => session.CatalogAsync(false, cancellation))
            .ConfigureAwait(false);

        var entries = catalog.Entries.ToList();
        var confirmed = entries.Count(entry => entry.IsVerified);
        var withManifest = entries.Count(entry => entry.HasManifest);

        var choices = entries
            .Select(entry => new Choice<ReleaseEntry>(entry, entry.Version, BrowseNote(entry), entry.HasManifest))
            .ToList();

        var status = $"{Format.Count(entries.Count)} builds   ·   " +
                     $"{Format.Count(withManifest)} downloadable   ·   {Format.Count(confirmed)} confirmed";

        while (true)
        {
            Header();

            if (!SelectionList<ReleaseEntry>.TryPick("Browse every known build", choices, out var entry,
                    $"{Hints.Move}   {Hints.Jump}   type to filter   {Hints.Open}   esc back",
                    status)) return;

            if (!await InspectAsync(entry, cancellation).ConfigureAwait(false)) return;
        }
    }

    private static string BrowseNote(ReleaseEntry entry)
    {
        var parts = new List<string>();

        if (entry.IsVerified) parts.Add("✓");
        else if (!entry.HasManifest) parts.Add("no manifest");
        else if (entry.IsKnownPatchy) parts.Add("!");

        parts.Add(entry.Season);
        if (entry.BuildDate.Length > 0) parts.Add(entry.BuildDate);
        if (entry.EngineVersion.Length > 0) parts.Add("UE " + entry.EngineVersion);
        if (entry.Notes.Length > 0) parts.Add(entry.Notes);
        return string.Join("   ", parts);
    }

    private async Task<bool> InspectAsync(ReleaseEntry entry, CancellationToken cancellation)
    {
        while (true)
        {
            Header();
            Output.Section(entry.Version);

            var panel = new Panel()
                .Row("Season", entry.Season, Theme.Cyan)
                .Row("Status", entry.IsVerified
                    ? "confirmed to download in full"
                    : entry.IsKnownPatchy
                        ? "Epic has pruned chunks, expect gaps"
                        : "not confirmed either way, run a check");

            if (entry.BuildDate.Length > 0) panel.Row("Build date", entry.BuildDate);
            if (entry.EngineVersion.Length > 0) panel.Row("Engine version", entry.EngineVersion);
            if (entry.NetCl.Length > 0) panel.Row("Net CL", entry.NetCl);
            if (entry.Notes.Length > 0) panel.Row("Notes", entry.Notes);

            panel.Row("Manifest", entry.HasManifest ? entry.ManifestId : "none archived",
                entry.HasManifest ? null : Theme.Warning);

            panel.Render();

            var actions = new List<Choice<BrowseAction>>
            {
                new(BrowseAction.Download, "Download this build", "pick install tags and a folder", entry.HasManifest),
                new(BrowseAction.Check, "Check availability", "sample the CDN for missing chunks", entry.HasManifest),
                new(BrowseAction.Info, "Show manifest details", "files, chunks and sizes", entry.HasManifest),
                new(BrowseAction.Tags, "Show install tags", "what each tag costs", entry.HasManifest),
                new(BrowseAction.Back, "Back to the list", string.Empty)
            };

            if (!SelectionList<BrowseAction>.TryPick("What next?", actions, out var action)) return true;

            switch (action)
            {
                case BrowseAction.Back:
                    return true;

                case BrowseAction.Download:
                    await DownloadEntryAsync(entry, cancellation).ConfigureAwait(false);
                    return true;

                case BrowseAction.Check:
                    await BuildView
                        .ShowAvailabilityAsync(session, await ResolveAsync(entry, cancellation).ConfigureAwait(false),
                            cancellation)
                        .ConfigureAwait(false);
                    ConsoleEx.PauseForKey("Press any key to go back.");
                    continue;

                case BrowseAction.Info:
                    BuildView.ShowInfo(await ResolveAsync(entry, cancellation).ConfigureAwait(false));
                    ConsoleEx.PauseForKey("Press any key to go back.");
                    continue;

                case BrowseAction.Tags:
                    BuildView.ShowTags((await ResolveAsync(entry, cancellation).ConfigureAwait(false)).Manifest);
                    ConsoleEx.PauseForKey("Press any key to go back.");
                    continue;
            }
        }
    }

    private void Settings() => new SettingsScreen(session).Run();

    private async Task<ResolvedManifest> ResolveAsync(ReleaseEntry entry, CancellationToken cancellation)
    {
        var catalog = await session.CatalogAsync(false, cancellation).ConfigureAwait(false);

        using (Spinner.Start($"Reading the manifest for {entry.Version}"))
        {
            return await ManifestResolver
                .ResolveAsync(entry.ManifestId, session.Http, session.Paths, catalog, cancellation)
                .ConfigureAwait(false);
        }
    }
}
