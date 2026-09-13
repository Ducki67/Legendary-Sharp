using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal static class UefnWorkflow
{
    public static async Task<UefnRelease?> MatchAsync(
        Session session,
        string buildVersion,
        CancellationToken cancellation)
    {
        var catalog = await session.UefnCatalogAsync(false, cancellation).ConfigureAwait(false);
        return catalog.MatchBuild(buildVersion);
    }

    public static async Task<int> InstallForBuildAsync(
        Session session,
        string installRoot,
        CancellationToken cancellation)
    {
        var build = await InstalledBuildLoader.OpenAsync(session, installRoot, cancellation).ConfigureAwait(false);
        var release = await MatchAsync(session, build.Record.BuildVersion, cancellation).ConfigureAwait(false);

        if (release is null)
        {
            Output.Blank();
            Output.Warn($"No archived UEFN release matches {build.Manifest.ShortVersion}.");
            Output.Hint("UEFN builds start at 24.01.");
            Output.Blank();
            return 1;
        }

        if (build.Record.UefnInstalledUtc is not null)
        {
            Output.Blank();
            Output.Info($"UEFN was installed here on {build.Record.UefnInstalledUtc:yyyy-MM-dd}.");
            Output.Hint("Running again restores anything missing.");
        }

        return await InstallAsync(session, build.Root, release, assumeYes: false, cancellation).ConfigureAwait(false);
    }

    public static async Task<int> InstallAsync(
        Session session,
        string installRoot,
        UefnRelease release,
        bool assumeYes,
        CancellationToken cancellation)
    {
        var resolved = await Spinner
            .Run($"Reading the UEFN manifest for {release.Display}",
                () => ManifestResolver.ResolveAsync(release.ManifestId, session.Http, session.Paths, null, cancellation))
            .ConfigureAwait(false);

        var request = new DownloadRequest
        {
            Source = resolved,
            InstallRoot = installRoot,
            Tags = [],
            IsAddon = true,
            AssumeYes = assumeYes
        };

        return await DownloadWorkflow.RunAsync(session, request, cancellation).ConfigureAwait(false);
    }

    public static async Task OfferAsync(
        Session session,
        string installRoot,
        string buildVersion,
        CancellationToken cancellation)
    {
        if (!session.Settings.OfferUefn || !ConsoleEx.IsInteractive) return;

        var record = InstallRecord.TryLoad(installRoot);
        if (record?.UefnInstalledUtc is not null) return;

        UefnRelease? release;

        try
        {
            release = await MatchAsync(session, buildVersion, cancellation).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return;
        }

        if (release is null) return;

        Output.Blank();
        Output.Section("UEFN is available for this build");
        Output.Hint($"{release.Display} matches this changelist and installs into the same folder.");
        Output.Blank();

        if (Prompt.Ask("Install UEFN now?", false) != true)
        {
            Output.Hint("You can add it later from My builds.");
            return;
        }

        await InstallAsync(session, installRoot, release, assumeYes: true, cancellation).ConfigureAwait(false);
    }
}
