using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal static class RepairWorkflow
{
    public static async Task<DownloadJob?> RunAsync(Session session, string installRoot, CancellationToken cancellation)
    {
        var build = await InstalledBuildLoader.OpenAsync(session, installRoot, cancellation).ConfigureAwait(false);
        var report = await VerifyWorkflow.RunAsync(build, cancellation).ConfigureAwait(false);

        if (report.IsHealthy) return null;

        var request = new DownloadRequest
        {
            Source = build.Source,
            InstallRoot = build.Root,
            Tags = build.Record.InstallTags.Count > 0 ? build.Record.InstallTags : null,
            RepairFiles = report.NeedsRepair
        };

        return await DownloadWorkflow.QueueAsync(session, request, cancellation).ConfigureAwait(false);
    }
}
