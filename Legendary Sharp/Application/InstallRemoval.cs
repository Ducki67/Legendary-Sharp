using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;

namespace Legendary_Sharp.Application;

internal static class InstallRemoval
{
    public static DiscardOutcome Uninstall(
        string root,
        IReadOnlyList<FileEntry> baseFiles,
        IReadOnlyList<FileEntry>? addonFiles,
        CancellationToken cancellation)
    {
        var union = new Dictionary<string, FileEntry>(StringComparer.Ordinal);

        foreach (var file in baseFiles) union.TryAdd(file.FileName, file);
        if (addonFiles is not null)
            foreach (var file in addonFiles) union.TryAdd(file.FileName, file);

        var outcome = InstallDiscard.DeleteAll(root, union.Values, cancellation);

        if (outcome.Failed == 0)
        {
            var state = InstallRecord.StateDirectory(root);

            try
            {
                if (Directory.Exists(state)) Directory.Delete(state, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }

        InstallDiscard.Tidy(root, union.Values);
        return outcome;
    }

    public static DiscardOutcome RemoveTags(
        string root,
        IReadOnlyList<FileEntry> removed,
        BuildManifest? addon,
        CancellationToken cancellation)
    {
        var protectedNames = addon is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(addon.Files.Select(file => file.FileName), StringComparer.Ordinal);

        var targets = removed.Where(file => !protectedNames.Contains(file.FileName)).ToList();
        var gone = new HashSet<string>(StringComparer.Ordinal);
        var outcome = InstallDiscard.DeleteAll(root, targets, cancellation, gone);
        var failed = outcome.Failed;

        try
        {
            using var resume = ResumeLog.Open(InstallRecord.StateDirectory(root));
            resume.Forget(gone);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            failed++;
        }

        InstallDiscard.Tidy(root, targets);
        return outcome with { Failed = failed };
    }
}
