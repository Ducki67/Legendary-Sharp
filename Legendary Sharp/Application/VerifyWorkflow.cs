using Legendary_Sharp.Downloader;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal static class VerifyWorkflow
{
    public static async Task<VerifyReport> RunAsync(OpenedBuild build, CancellationToken cancellation)
    {
        var files = InstalledBuildLoader.SelectedFiles(build);

        Output.Section("Verify");

        new Panel()
            .Row("Build", build.Manifest.ShortVersion, Theme.Cyan)
            .Row("Folder", build.Root)
            .Row("Files to check", Format.Count(files.Count))
            .Row("Bytes to hash", Format.Bytes(files.Sum(file => file.Size)))
            .Render();

        using var display = new ProgressDisplay("Verify", build.Manifest.ShortVersion);

        var report = await InstallVerifier
            .VerifyAsync(files, build.Root, progress => display.Update(ToDownloadProgress(progress, files.Count)),
                cancellation)
            .ConfigureAwait(false);

        display.Finish();
        Present(report);
        return report;
    }

    public static void Present(VerifyReport report)
    {
        Output.Blank();

        if (report.IsHealthy)
        {
            Output.Success($"All {Format.Count(report.Healthy)} files match the manifest.");
            Output.Blank();
            return;
        }

        Output.Warn($"{Format.Count(report.Broken)} files need attention.");
        Output.Blank();

        new Panel()
            .Row("Healthy", Format.Count(report.Healthy), Theme.Success)
            .Row("Missing", Format.Count(report.Missing.Count), Theme.Danger)
            .Row("Damaged", Format.Count(report.Damaged.Count), Theme.Warning)
            .Render();

        Output.Blank();

        foreach (var file in report.NeedsRepair.Take(12))
            Output.Bullet(Format.TruncateStart(file.FileName, ConsoleEx.Width - 10));

        if (report.Broken > 12) Output.Detail($"and {Format.Count(report.Broken - 12)} more");

        Output.Blank();
    }

    private static DownloadProgress ToDownloadProgress(VerifyProgress progress, int total)
    {
        var rate = progress.Elapsed.TotalSeconds > 0.5d
            ? progress.BytesDone / progress.Elapsed.TotalSeconds
            : 0d;

        var remaining = progress.BytesTotal - progress.BytesDone;
        var eta = rate > 1024d && remaining > 0 ? TimeSpan.FromSeconds(remaining / rate) : TimeSpan.Zero;

        return new DownloadProgress(
            progress.BytesDone,
            progress.BytesTotal,
            progress.BytesDone,
            progress.BytesTotal,
            progress.FilesDone,
            total,
            0,
            0,
            0,
            0,
            rate,
            rate,
            progress.Elapsed,
            eta,
            progress.CurrentFile);
    }
}
