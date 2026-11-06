using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed class BrowseScreen(Session session, Func<ReleaseEntry, CancellationToken, Task> download)
{
    private enum BrowseAction
    {
        Download,
        Check,
        Info,
        Tags,
        Back
    }

    public async Task RunAsync(CancellationToken cancellation)
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
            InteractiveShell.Header();

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
            InteractiveShell.Header();
            Output.Section(entry.Version);

            var panel = new Panel()
                .Row("Season", entry.Season, Theme.Cyan)
                .Row("Status", entry.IsVerified
                    ? "confirmed to download in full"
                    : entry.IsKnownPatchy
                        ? VerifiedBuilds.MissingNote(entry.Version) is { } note
                            ? $"Epic has pruned chunks, {note} whatever tags you pick"
                            : "Epic has pruned chunks, expect gaps"
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
                    await download(entry, cancellation).ConfigureAwait(false);
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
