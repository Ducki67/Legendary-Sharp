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

        var (fetched, origin, failure) = await FetchAsync(manifestId, http, paths, cancellation).ConfigureAwait(false);

        if (fetched is null)
            throw new InvalidOperationException(
                $"{release?.Version ?? manifestId} could not be read from any manifest archive or Epic mirror" +
                (failure is null ? "." : $", the last error was: {failure.Message}"));

        return new ResolvedManifest(fetched, manifestId, origin, release);
    }

    private static async Task<(BuildManifest? Manifest, string Origin, Exception? Failure)> FetchAsync(
        string manifestId,
        ChunkSource http,
        AppPaths paths,
        CancellationToken cancellation)
    {
        var cached = CachePath(manifestId, paths);

        if (File.Exists(cached))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(cached, cancellation).ConfigureAwait(false);
                return (ManifestLoader.Parse(bytes), "local cache", null);
            }
            catch (Exception error) when (!cancellation.IsCancellationRequested && error is not OutOfMemoryException)
            {
                TryDelete(cached);
            }
        }

        var candidates = new List<(string Url, string Origin)>
        {
            ($"{FortniteConstants.ReleaseManifestUrl}/{manifestId}.manifest", "polynite/fn-releases"),
            ($"{FortniteConstants.ArchiveManifestUrl}/{manifestId}.manifest", "VastBlast/FortniteManifestArchive"),
            ($"{FortniteConstants.UefnManifestUrl}/{manifestId}.manifest", "Mast3rGamers/UEFN-releases")
        };

        candidates.AddRange(FortniteConstants.CloudMirrors
            .Select(mirror => ($"{mirror}/{manifestId}.manifest", new Uri(mirror).Host)));

        Exception? failure = null;

        foreach (var (url, origin) in candidates)
        {
            try
            {
                var bytes = await http.GetAsync(url, cancellation).ConfigureAwait(false);
                var manifest = ManifestLoader.Parse(bytes);
                TryCache(cached, bytes);
                return (manifest, origin, null);
            }
            catch (Exception error) when (!cancellation.IsCancellationRequested)
            {
                if (error is not HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound })
                    failure = error;
            }
        }

        return (null, string.Empty, failure);
    }

    private static void TryCache(string path, byte[] bytes)
    {
        try
        {
            AtomicFile.WriteAllBytes(path, bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string CachePath(string manifestId, AppPaths paths) =>
        Path.Combine(paths.Manifests, manifestId + ".manifest");
}
