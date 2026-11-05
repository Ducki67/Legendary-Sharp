using Legendary_Sharp.Downloader;
using Legendary_Sharp.Fortnite;

namespace Legendary_Sharp.Application;

internal sealed record AdoptionResult(bool Adopted, int Healthy, int Missing, int Damaged, string? Refusal);

internal static class BuildAdoption
{
    public static async Task<AdoptionResult> AdoptAsync(
        Session session,
        string root,
        BuildMatch match,
        IReadOnlyList<string> tags,
        CancellationToken cancellation)
    {
        var manifest = match.Source.Manifest;
        var files = DownloadPlanner.SelectFiles(manifest, tags.Count == 0 ? null : tags);

        var report = await VerifyWorkflow
            .CheckAsync(files, root, manifest.ShortVersion, "Checking files", cancellation)
            .ConfigureAwait(false);

        var missing = report.Missing.Count;
        var damaged = report.Damaged.Count;

        if (files.Count == 0 || (double)report.Healthy / files.Count < BuildMatcher.AcceptThreshold)
            return new AdoptionResult(false, report.Healthy, missing, damaged,
                $"These files do not match {manifest.ShortVersion}. Nothing was added.");

        var broken = new HashSet<string>(
            report.Missing.Concat(report.Damaged).Select(file => file.FileName), StringComparer.Ordinal);

        cancellation.ThrowIfCancellationRequested();

        var state = InstallRecord.StateDirectory(root);
        var stateExisted = Directory.Exists(state);
        var manifestPath = InstallRecord.ManifestPath(root);
        var logPath = Path.Combine(state, ResumeLog.BaseLog);
        var recordPath = InstallRecord.RecordPath(root);
        var created = new[] { manifestPath, logPath, recordPath }.Where(path => !File.Exists(path)).ToList();

        try
        {
            Directory.CreateDirectory(state);

            DownloadWorkflow.CopyManifest(match.Source, manifestPath);

            if (!File.Exists(manifestPath))
                throw new IOException("the manifest could not be saved");

            using (var resume = ResumeLog.Open(state))
            {
                resume.MarkComplete(files.Where(file => !broken.Contains(file.FileName)));
            }

            var record = new InstallRecord
            {
                BuildVersion = manifest.BuildVersion,
                ManifestId = match.Source.ManifestId,
                AppName = manifest.AppName,
                LaunchExe = manifest.LaunchExe,
                InstallTags = [.. tags],
                InstallSize = files.Sum(file => file.Size),
                CompletedUtc = missing + damaged == 0 ? DateTime.UtcNow : null,
                Adopted = true
            };

            record.Save(root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Undo(state, stateExisted, created);

            return new AdoptionResult(false, report.Healthy, missing, damaged,
                $"Could not write the build state: {error.Message}. Nothing was added.");
        }

        return new AdoptionResult(true, report.Healthy, missing, damaged, null);
    }

    private static void Undo(string state, bool stateExisted, IEnumerable<string> created)
    {
        try
        {
            if (!stateExisted)
            {
                if (Directory.Exists(state)) Directory.Delete(state, true);
                return;
            }

            foreach (var path in created)
                if (File.Exists(path))
                    File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
