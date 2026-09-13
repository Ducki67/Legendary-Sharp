using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;

namespace Legendary_Sharp.Application;

internal sealed record OpenedBuild(string Root, InstallRecord Record, ResolvedManifest Source)
{
    public BuildManifest Manifest => Source.Manifest;
}

internal static class InstalledBuildLoader
{
    public static async Task<OpenedBuild> OpenAsync(Session session, string path, CancellationToken cancellation)
    {
        var root = Path.GetFullPath(Interface.Prompt.Clean(path));

        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"\"{root}\" does not exist.");

        var record = InstallRecord.TryLoad(root)
                     ?? throw new InvalidOperationException(
                         $"\"{root}\" was not installed by {AppInfo.Name}, there is no {InstallRecord.FolderName} folder.");

        var local = InstallRecord.ManifestPath(root);

        if (File.Exists(local))
        {
            var manifest = await ManifestLoader.LoadFileAsync(local, cancellation).ConfigureAwait(false);
            return new OpenedBuild(root, record,
                new ResolvedManifest(manifest, record.ManifestId, "install folder", null));
        }

        if (record.ManifestId.Length == 0)
            throw new InvalidOperationException($"\"{root}\" has no stored manifest and no manifest id to fetch one.");

        var catalog = await session.CatalogAsync(false, cancellation).ConfigureAwait(false);
        var resolved = await ManifestResolver
            .ResolveAsync(record.ManifestId, session.Http, session.Paths, catalog, cancellation)
            .ConfigureAwait(false);

        return new OpenedBuild(root, record, resolved);
    }

    public static List<FileEntry> SelectedFiles(OpenedBuild build) =>
        build.Record.InstallTags.Count == 0
            ? DownloadPlannerFiles(build, null)
            : DownloadPlannerFiles(build, build.Record.InstallTags);

    private static List<FileEntry> DownloadPlannerFiles(OpenedBuild build, IReadOnlyList<string>? tags) =>
        Downloader.DownloadPlanner.SelectFiles(build.Manifest, tags);
}
