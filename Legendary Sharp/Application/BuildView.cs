using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal static class BuildView
{
    public static void ShowInfo(ResolvedManifest resolved)
    {
        var manifest = resolved.Manifest;

        Output.Banner(manifest.ShortVersion, resolved.Release?.Season ?? manifest.AppName);

        var panel = new Panel()
            .Row("Build version", manifest.BuildVersion, Theme.Cyan)
            .Row("App name", manifest.AppName)
            .Row("Manifest id", resolved.ManifestId)
            .Row("Source", resolved.Origin)
            .Row("Format", $"{manifest.Format.ToString().ToLowerInvariant()}, feature level {manifest.FeatureLevel}");

        if (manifest.BuildId.Length > 0) panel.Row("Build id", manifest.BuildId);
        panel.Row("Client exe", manifest.LaunchExe);

        if (resolved.Release is { } release)
        {
            var known = new (string Key, string Value)[]
            {
                ("Season", release.Season),
                ("Engine version", release.EngineVersion),
                ("Net CL", release.NetCl),
                ("Build date", release.BuildDate),
                ("Notes", release.Notes)
            }.Where(item => item.Value.Length > 0).ToList();

            if (known.Count > 0)
            {
                panel.Gap();
                foreach (var (key, value) in known) panel.Row(key, value);
            }
        }

        panel.Gap()
            .Row("Files", Format.Count(manifest.Files.Count))
            .Row("Chunks", Format.Count(manifest.Chunks.Count))
            .Row("Download size", Format.Bytes(manifest.DownloadSize))
            .Row("Size on disk", Format.Bytes(manifest.InstallSize))
            .Row("Install tags", manifest.InstallTags.Count == 0
                ? "none, this build downloads whole"
                : $"{manifest.InstallTags.Count} available")
            .Render();

        Output.Section("10 largest files");

        var table = new Table()
            .Add("Size", Table.Alignment.Right, 12)
            .Add("Tags", maxWidth: 24)
            .Add("File");

        foreach (var file in manifest.Files.OrderByDescending(file => file.Size).Take(10))
            table.Row(Format.Bytes(file.Size), string.Join(",", file.InstallTags), file.FileName);

        table.Render();
        Output.Blank();
    }

    public static void ShowTags(BuildManifest manifest)
    {
        var groups = InstallTagCatalog.Summarise(manifest);

        Output.Banner("Install tags", manifest.ShortVersion);

        if (groups.Count <= 1)
        {
            Output.Info("This build has no install tags, so it always downloads whole.");
            Output.Pair("Size on disk", Format.Bytes(manifest.InstallSize));
            Output.Blank();
            return;
        }

        var table = new Table()
            .Add("Tag", maxWidth: 30)
            .Add("Group", maxWidth: 14)
            .Add("Files", Table.Alignment.Right, 7)
            .Add("Size on disk", Table.Alignment.Right, 12)
            .Add("Contents");

        foreach (var group in groups)
            table.Row(
                group.Display,
                DescribeKind(group.Kind),
                Format.Count(group.Files),
                Format.Bytes(group.InstallSize),
                group.Description);

        table.Render();

        if (groups.Any(group => group.Tags.Count > 1))
        {
            Output.Blank();
            Output.Hint("Tags on the same row are aliases, they select exactly the same files.");
        }

        Output.Section("Presets");

        var presets = new Table()
            .Add("Preset", maxWidth: 16)
            .Add("Size on disk", Table.Alignment.Right, 12)
            .Add("What you get");

        foreach (var preset in TagPresets.All)
            presets.Row(
                preset.Name,
                Format.Bytes(InstallTagCatalog.SizeOf(manifest, preset.Resolve(groups))),
                preset.Description);

        presets.Render();
        Output.Blank();
    }

    public static async Task ShowAvailabilityAsync(
        Session session,
        ResolvedManifest resolved,
        CancellationToken cancellation)
    {
        var plan = DownloadPlanner.Create(resolved.Manifest, null);
        const int sample = 64;

        Output.Banner("Availability", resolved.Manifest.ShortVersion);

        new Panel()
            .Row("Chunks in the build", Format.Count(plan.ChunkCount), Theme.Cyan)
            .Row("Download size", Format.Bytes(plan.DownloadSize))
            .Render();

        Output.Blank();

        AvailabilityReport report;
        using (Spinner.Start($"Probing {sample} chunks across every mirror"))
        {
            report = await AvailabilityProbe
                .RunAsync(plan, session.Http, sample, cancellation)
                .ConfigureAwait(false);
        }

        if (report.IsOffline)
        {
            Output.Error("Could not reach any of Epic's CDN mirrors.");
            Output.Hint("Check your connection, a VPN or firewall can also block them.");
            Output.Blank();
            return;
        }

        var panel = new Panel()
            .Row("Sampled", Format.Count(report.Sampled))
            .Row("Still hosted", $"{Format.Count(report.Available)}  ({Format.Percent(report.Fraction)})",
                report.IsComplete ? Theme.Success : Theme.Warning);

        if (report.Unreachable > 0)
            panel.Row("Not checked", $"{Format.Count(report.Unreachable)}  (no mirror answered)", Theme.Muted);

        panel.Render();

        Output.Blank();

        if (report.IsComplete) Output.Success("This build looks fully downloadable.");
        else if (report.IsHopeless) Output.Error("Nothing in the sample is hosted, this build is gone from the CDN.");
        else Output.Warn("Epic has pruned part of this build, expect missing files.");

        Output.Blank();
    }

    private static string DescribeKind(TagKind kind) => kind switch
    {
        TagKind.Untagged => "required",
        TagKind.Core => "core",
        TagKind.BattleRoyale => "battle royale",
        TagKind.SaveTheWorld => "save the world",
        TagKind.HighRes => "high res",
        TagKind.OnDemand => "streamed",
        TagKind.GameMode => "game mode",
        TagKind.Language => "language",
        _ => "optional"
    };
}
