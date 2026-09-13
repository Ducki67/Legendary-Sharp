using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal static class RepairWorkflow
{
    public static async Task<int> RunAsync(Session session, string installRoot, CancellationToken cancellation)
    {
        var build = await InstalledBuildLoader.OpenAsync(session, installRoot, cancellation).ConfigureAwait(false);
        var report = await VerifyWorkflow.RunAsync(build, cancellation).ConfigureAwait(false);

        if (report.IsHealthy) return 0;

        var request = new DownloadRequest
        {
            Source = build.Source,
            InstallRoot = build.Root,
            Tags = build.Record.InstallTags,
            RepairFiles = report.NeedsRepair
        };

        return await DownloadWorkflow.RunAsync(session, request, cancellation).ConfigureAwait(false);
    }
}
