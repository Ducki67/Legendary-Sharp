namespace Legendary_Sharp.Fortnite;

internal sealed record InstalledBuild(string Path, InstallRecord Record)
{
    public string Name => System.IO.Path.GetFileName(Path);

    public bool HasClient => File.Exists(System.IO.Path.Combine(
        Path, FortniteConstants.BinariesFolder, FortniteConstants.ShippingExecutable));

    public string Status => Record.IsComplete ? "complete" : "incomplete";
}

internal static class BuildLibrary
{
    public static IReadOnlyList<InstalledBuild> Scan(IEnumerable<string> roots)
    {
        var found = new List<InstalledBuild>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            Add(found, seen, root);

            foreach (var child in SafeDirectories(root)) Add(found, seen, child);
        }

        return [.. found.OrderBy(build => build.Record.BuildVersion, StringComparer.Ordinal)];
    }

    public static bool IsBuildFolder(string path) => InstallRecord.TryLoad(path) is not null;

    private static void Add(List<InstalledBuild> found, HashSet<string> seen, string path)
    {
        var record = InstallRecord.TryLoad(path);
        if (record is null) return;

        var full = Path.GetFullPath(path);
        if (!seen.Add(full)) return;

        found.Add(new InstalledBuild(full, record));
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
