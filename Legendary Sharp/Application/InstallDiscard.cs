using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed record DiscardOutcome(int Deleted, int Failed)
{
    public string Describe(string verb)
    {
        var text = $"{verb} {Format.Files(Deleted)}.";

        return Failed > 0
            ? $"{text} {Format.Files(Failed)} {(Failed == 1 ? "is" : "are")} in use and {(Failed == 1 ? "was" : "were")} left in the folder."
            : text;
    }
}

internal static class InstallDiscard
{
    private const int Attempts = 3;

    public static bool IsAllowed(DownloadRequest request, string root)
    {
        if (request.RepairFiles is not null || request.IsAddon) return false;

        var record = InstallRecord.TryLoad(root);
        if (record is { Extended: true } or { Adopted: true }) return false;

        return record is null || (!record.IsComplete && record.ManifestId == request.Source.ManifestId);
    }

    public static DiscardOutcome Run(DownloadRequest request, string root)
    {
        var state = InstallRecord.StateDirectory(root);
        if (!Directory.Exists(root) || !Directory.Exists(state)) return new DiscardOutcome(0, 0);

        var created = new List<FileEntry>();
        var failed = 0;
        var deleted = 0;

        try
        {
            using var resume = ResumeLog.Open(state);

            created.AddRange(resume.CreatedFiles(request.Source.Manifest));

            var outcome = DeleteAll(root, created, CancellationToken.None);
            deleted = outcome.Deleted;
            failed = outcome.Failed;

            if (failed == 0)
            {
                resume.Clear();

                var record = InstallRecord.TryLoad(root);

                if (record is { IsComplete: false } && record.ManifestId == request.Source.ManifestId)
                {
                    File.Delete(InstallRecord.RecordPath(root));
                    File.Delete(InstallRecord.ManifestPath(root));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            failed++;
        }

        Tidy(root, created);
        return new DiscardOutcome(deleted, failed);
    }

    internal static DiscardOutcome DeleteAll(
        string root,
        IEnumerable<FileEntry> files,
        CancellationToken cancellation,
        ISet<string>? gone = null)
    {
        var deleted = 0;
        var failed = 0;

        foreach (var file in files)
        {
            cancellation.ThrowIfCancellationRequested();

            var path = Path.Combine(root, file.ToNativePath());

            if (!new FileInfo(path).Exists) gone?.Add(file.FileName);
            else if (Delete(path))
            {
                gone?.Add(file.FileName);
                deleted++;
            }
            else failed++;
        }

        return new DiscardOutcome(deleted, failed);
    }

    private static bool Delete(string path)
    {
        var info = new FileInfo(path);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (info.IsReadOnly) info.IsReadOnly = false;
                info.Delete();
                return true;
            }
            catch (IOException error) when (FileLocks.IsLockViolation(error) && attempt < Attempts)
            {
                Thread.Sleep(250 * attempt);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    internal static void Tidy(string root, IEnumerable<FileEntry> files)
    {
        if (!Directory.Exists(root)) return;

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var directories = new HashSet<string>(StringComparer.Ordinal) { InstallRecord.StateDirectory(root) };

        foreach (var file in files)
        {
            var directory = Path.GetDirectoryName(Path.Combine(full, file.ToNativePath()));

            while (directory is not null && directory.Length > full.Length && directories.Add(directory))
                directory = Path.GetDirectoryName(directory);
        }

        foreach (var directory in directories.OrderByDescending(path => path.Length)) RemoveIfEmpty(directory);
    }

    private static bool RemoveIfEmpty(string directory)
    {
        try
        {
            if (!Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any()) return false;
            Directory.Delete(directory, false);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
