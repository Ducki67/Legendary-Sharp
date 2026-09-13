namespace Legendary_Sharp.Downloader.Manifest;

internal static class ManifestLoader
{
    public static BuildManifest Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) throw new InvalidDataException("The manifest file is empty.");
        if (BinaryManifestReader.Matches(data)) return BinaryManifestReader.Read(data);
        if (JsonManifestReader.Matches(data)) return JsonManifestReader.Read(data);

        throw new InvalidDataException(
            "Unrecognised manifest: expected an Epic binary manifest or a JSON manifest.");
    }

    public static async Task<BuildManifest> LoadFileAsync(string path, CancellationToken cancellation)
    {
        var data = await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false);
        return Parse(data);
    }
}
