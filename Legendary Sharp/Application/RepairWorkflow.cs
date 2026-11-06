using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal static class RepairWorkflow
{
    public static async Task<DownloadJob?> RunAsync(Session session, string installRoot, CancellationToken cancellation)
    {
        var label = InstallRecord.TryLoad(Path.GetFullPath(Prompt.Clean(installRoot))) is { BuildVersion.Length: > 0 } known
            ? Naming.ShortenBuildVersion(known.BuildVersion)
            : "the build";

        OpenedBuild build;
        using (Spinner.Start($"Opening {label}"))
            build = await InstalledBuildLoader.OpenAsync(session, installRoot, cancellation).ConfigureAwait(false);

        var report = await VerifyWorkflow.RunAsync(build, cancellation).ConfigureAwait(false);

        if (report.IsHealthy) return null;

        var request = new DownloadRequest
        {
            Source = build.Source,
            InstallRoot = build.Root,
            Tags = build.Record.TagFilter,
            RepairFiles = report.NeedsRepair
        };

        return await DownloadWorkflow.QueueAsync(session, request, cancellation).ConfigureAwait(false);
    }
}
