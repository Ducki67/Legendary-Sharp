using Legendary_Sharp.Downloader;

namespace Legendary_Sharp.Fortnite;

internal sealed record InstalledBuild(string Path, InstallRecord Record)
{
    public string Name => System.IO.Path.GetFileName(Path);

    public string Status => Record.IsComplete
        ? Record.HasUnavailable ? $"complete, {Record.Unavailable.Count} not hosted" : "complete"
        : "incomplete";
}

internal sealed record UnmanagedBuild(string Path, string Library);

internal static class BuildLibrary
{
    private const int MaxDepth = 2;

    public static IReadOnlyList<InstalledBuild> Scan(IEnumerable<string> roots) => ScanAll(roots).Managed;

    public static (IReadOnlyList<InstalledBuild> Managed, IReadOnlyList<UnmanagedBuild> Unmanaged) ScanAll(
        IEnumerable<string> roots)
    {
        var managed = new List<InstalledBuild>();
        var unmanaged = new List<UnmanagedBuild>();
        var seenManaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUnmanaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            var library = FolderPath.Normalize(root);

            Walk(root, 0, path =>
            {
                var record = InstallRecord.TryLoad(path);
                if (record is not null)
                {
                    var full = FolderPath.Normalize(path);
                    if (seenManaged.Add(full)) managed.Add(new InstalledBuild(full, record));
                    return true;
                }

                if (!LooksLikeBuild(path)) return false;

                var folder = FolderPath.Normalize(path);
                if (seenUnmanaged.Add(folder)) unmanaged.Add(new UnmanagedBuild(folder, library));
                return true;
            });
        }

        var managedPaths = new HashSet<string>(managed.Select(build => build.Path), StringComparer.OrdinalIgnoreCase);
        unmanaged.RemoveAll(build => managedPaths.Contains(build.Path));

        return ([.. managed.OrderBy(build => build.Record.BuildVersion, StringComparer.Ordinal)], unmanaged);
    }

    private static bool LooksLikeBuild(string path)
    {
        try
        {
            return FortniteConstants.BuildContents.All(name => Directory.Exists(Path.Combine(path, name)));
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static void Walk(string path, int depth, Func<string, bool> visit)
    {
        if (visit(path) || depth >= MaxDepth) return;

        foreach (var child in SafeDirectories(path))
        {
            if (Path.GetFileName(child).StartsWith('.') || IsHiddenOrLinked(child)) continue;
            Walk(child, depth + 1, visit);
        }
    }

    private static bool IsHiddenOrLinked(string path)
    {
        try
        {
            return (new DirectoryInfo(path).Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden)) != 0;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try
        {
            return Directory.GetDirectories(root);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
