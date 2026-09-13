using Legendary_Sharp.Application;
using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Fortnite;

internal sealed record ResolvedManifest(
    BuildManifest Manifest,
    string ManifestId,
    string Origin,
    ReleaseEntry? Release);

internal static class ManifestResolver
{
    public static async Task<ResolvedManifest> ResolveAsync(
        string reference,
        ChunkSource http,
        AppPaths paths,
        ReleaseCatalog? catalog,
        CancellationToken cancellation)
    {
        var cleaned = Interface.Prompt.Clean(reference);

        if (File.Exists(cleaned))
        {
            var manifest = await ManifestLoader.LoadFileAsync(cleaned, cancellation).ConfigureAwait(false);
            var id = Path.GetFileNameWithoutExtension(cleaned);
            return new ResolvedManifest(manifest, id, cleaned, catalog?.Find(id));
        }

        var release = catalog?.Find(cleaned);
        var manifestId = release?.ManifestId ?? cleaned;

        if (release is not null && !release.HasManifest)
            throw new InvalidOperationException(
                $"{release.Version} has no manifest id in the release index, so it cannot be downloaded.");

        if (manifestId.Length == 0 || manifestId.Any(Path.GetInvalidFileNameChars().Contains))
            throw new InvalidOperationException($"\"{reference}\" is not a build, a manifest id or an existing file.");

        var fetched = await FetchAsync(manifestId, http, paths, cancellation).ConfigureAwait(false);

        if (fetched is null)
            throw new InvalidOperationException(release is null
                ? $"\"{reference}\" is not a build in the release index, a manifest id that could be downloaded, " +
                  "or a file on disk. Search for the build you want with \"list\"."
                : $"{release.Version} is in the release index but manifest {manifestId} could not be downloaded " +
                  "from fn-releases or any Epic mirror.");

        return new ResolvedManifest(ManifestLoader.Parse(fetched.Value.Bytes), manifestId, fetched.Value.Origin, release);
    }

    public static async Task<byte[]> LoadCachedAsync(string manifestId, AppPaths paths, CancellationToken cancellation)
    {
        var path = CachePath(manifestId, paths);
        return await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false);
    }

    private static async Task<(byte[] Bytes, string Origin)?> FetchAsync(
        string manifestId,
        ChunkSource http,
        AppPaths paths,
        CancellationToken cancellation)
    {
        var cached = CachePath(manifestId, paths);
        if (File.Exists(cached))
            return (await File.ReadAllBytesAsync(cached, cancellation).ConfigureAwait(false), "local cache");

        var candidates = new List<(string Url, string Origin)>
        {
            ($"{FortniteConstants.ReleaseManifestUrl}/{manifestId}.manifest", "polynite/fn-releases"),
            ($"{FortniteConstants.ArchiveManifestUrl}/{manifestId}.manifest", "VastBlast/FortniteManifestArchive"),
            ($"{FortniteConstants.UefnManifestUrl}/{manifestId}.manifest", "Mast3rGamers/UEFN-releases")
        };

        candidates.AddRange(FortniteConstants.CloudMirrors
            .Select(mirror => ($"{mirror}/{manifestId}.manifest", new Uri(mirror).Host)));

        foreach (var (url, origin) in candidates)
        {
            try
            {
                var bytes = await http.GetAsync(url, cancellation).ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
                await File.WriteAllBytesAsync(cached, bytes, cancellation).ConfigureAwait(false);
                return (bytes, origin);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
            }
        }

        return null;
    }

    private static string CachePath(string manifestId, AppPaths paths) =>
        Path.Combine(paths.Manifests, manifestId + ".manifest");
}
