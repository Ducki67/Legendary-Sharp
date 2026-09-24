namespace Legendary_Sharp.Downloader.Manifest;

internal sealed class BuildManifest
{
    private readonly Dictionary<ChunkGuid, ChunkInfo> _chunkLookup;
    private readonly Lazy<string[]> _installTags;

    public BuildManifest(
        int featureLevel,
        string appName,
        string buildVersion,
        string launchExe,
        string launchCommand,
        string buildId,
        IReadOnlyList<ChunkInfo> chunks,
        IReadOnlyList<FileEntry> files,
        IReadOnlyDictionary<string, string> customFields,
        ManifestFormat format)
    {
        FeatureLevel = featureLevel;
        AppName = appName;
        BuildVersion = buildVersion;
        LaunchExe = launchExe;
        LaunchCommand = launchCommand;
        BuildId = buildId;
        Chunks = chunks;
        Files = files;
        CustomFields = customFields;
        Format = format;

        foreach (var file in files) Naming.EnsureSafePath(file.FileName);

        _chunkLookup = new Dictionary<ChunkGuid, ChunkInfo>(chunks.Count);
        foreach (var chunk in chunks) _chunkLookup[chunk.Guid] = chunk;

        _installTags = new Lazy<string[]>(() =>
        {
            var tags = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            foreach (var tag in file.InstallTags)
                tags.Add(tag);
            return [.. tags];
        });
    }

    public int FeatureLevel { get; }

    public string AppName { get; }

    public string BuildVersion { get; }

    public string LaunchExe { get; }

    public string LaunchCommand { get; }

    public string BuildId { get; }

    public IReadOnlyList<ChunkInfo> Chunks { get; }

    public IReadOnlyList<FileEntry> Files { get; }

    public IReadOnlyDictionary<string, string> CustomFields { get; }

    public ManifestFormat Format { get; }

    public IReadOnlyList<string> InstallTags => _installTags.Value;

    public bool HasUntaggedFiles => Files.Any(file => file.InstallTags.Length == 0);

    public long InstallSize => Files.Sum(file => file.Size);

    public long DownloadSize => Chunks.Sum(chunk => chunk.CompressedSize);

    public string ShortVersion => Naming.ShortenBuildVersion(BuildVersion);

    public ChunkInfo Chunk(ChunkGuid guid) =>
        _chunkLookup.TryGetValue(guid, out var chunk)
            ? chunk
            : throw new InvalidDataException($"Manifest references unknown chunk {guid.ToHex()}.");
}

internal enum ManifestFormat
{
    Binary,
    Json
}

internal static class Naming
{
    private static readonly char[] Separators = ['/', '\\'];

    private static readonly char[] UnsafeCharacters =
        [.. Path.GetInvalidFileNameChars().Except(Separators), ':'];

    public static void EnsureSafePath(string fileName)
    {
        if (!IsSafePath(fileName))
            throw new InvalidDataException(
                $"The manifest lists a file outside the install folder, \"{fileName}\", so it was not used.");
    }

    public static bool IsSafePath(string fileName)
    {
        if (fileName.Length == 0 || Array.IndexOf(Separators, fileName[0]) >= 0) return false;
        if (fileName.IndexOfAny(UnsafeCharacters) >= 0) return false;

        foreach (var segment in fileName.Split(Separators))
            if (segment == "..") return false;

        return true;
    }

    public static string ShortenBuildVersion(string buildVersion)
    {
        if (buildVersion.Length == 0) return "unknown";

        var value = buildVersion;
        const string releasePrefix = "++Fortnite+Release-";
        if (value.StartsWith(releasePrefix, StringComparison.Ordinal)) value = value[releasePrefix.Length..];
        else if (value.StartsWith("++Fortnite+", StringComparison.Ordinal)) value = value["++Fortnite+".Length..];

        const string windowsSuffix = "-Windows";
        if (value.EndsWith(windowsSuffix, StringComparison.Ordinal)) value = value[..^windowsSuffix.Length];

        return value.Length == 0 ? buildVersion : value;
    }

    public static string SanitiseFolder(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '-' : character);
        return builder.ToString().Trim();
    }
}
