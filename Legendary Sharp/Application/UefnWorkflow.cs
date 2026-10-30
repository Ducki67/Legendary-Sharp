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

    public static async Task<DownloadJob?> InstallForBuildAsync(
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
            return null;
        }

        if (build.Record.UefnInstalledUtc is not null)
        {
            Output.Blank();
            Output.Info($"UEFN was installed here on {build.Record.UefnInstalledUtc:yyyy-MM-dd}.");
            Output.Hint("Running again restores anything missing.");
        }

        return await InstallAsync(session, build.Root, release, cancellation).ConfigureAwait(false);
    }

    public static async Task<DownloadJob?> InstallAsync(
        Session session,
        string installRoot,
        UefnRelease release,
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
            Tags = null,
            IsAddon = true
        };

        return await DownloadWorkflow.QueueAsync(session, request, cancellation).ConfigureAwait(false);
    }
}
