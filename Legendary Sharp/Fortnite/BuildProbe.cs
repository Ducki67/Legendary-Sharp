using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Legendary_Sharp.Application;
using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Fortnite;

internal sealed record BuildProbe(string Version, string? Changelist, string Source, string? EgstoreManifest);

internal sealed class ProbeCacheEntry
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;

    [JsonPropertyName("length")] public long Length { get; set; }

    [JsonPropertyName("ticks")] public long Ticks { get; set; }

    [JsonPropertyName("version")] public string? Version { get; set; }

    [JsonPropertyName("changelist")] public string? Changelist { get; set; }
}

internal sealed class ProbeCacheFile
{
    [JsonPropertyName("entries")] public List<ProbeCacheEntry> Entries { get; set; } = [];
}

internal static partial class BuildProbes
{
    private const string Prefix = "++Fortnite+Release-";
    private const int BufferSize = 1024 * 1024;
    private const int Overlap = 256;
    private const int Window = 128;

    private static readonly byte[] AsciiPrefix = Encoding.ASCII.GetBytes(Prefix);
    private static readonly byte[] WidePrefix = Encoding.Unicode.GetBytes(Prefix);

    [GeneratedRegex(@"^(\d+\.\d+)(?:-CL-(\d+))?")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"Release-(\d+\.\d+)(?:-CL-(\d+))?")]
    private static partial Regex ManifestVersionPattern();

    private static readonly bool[] Modes = [false, true];

    public static IReadOnlyDictionary<string, BuildProbe?> ReadAll(IEnumerable<string> roots, AppPaths paths)
    {
        var cache = LoadCache(paths);
        var dirty = false;
        var results = new Dictionary<string, BuildProbe?>();

        try
        {
            foreach (var root in roots)
            {
                if (results.ContainsKey(root)) continue;
                results[root] = ProbeEgstore(root, cache, ref dirty)
                                ?? ProbeExecutable(root, cache, ref dirty)
                                ?? ProbeBuildVersion(root);
            }
        }
        finally
        {
            if (dirty) SaveCache(paths, cache);
        }

        return results;
    }

    private static BuildProbe? ProbeEgstore(string root, ProbeCacheFile cache, ref bool dirty)
    {
        var folder = Path.Combine(root, FortniteConstants.EgstoreFolder);
        if (!Directory.Exists(folder)) return null;

        FileInfo[] files;
        try
        {
            files = [.. new DirectoryInfo(folder).GetFiles("*.manifest").OrderByDescending(file => file.LastWriteTimeUtc)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var file in files)
        {
            var entry = Lookup(cache, file);

            if (entry is null)
            {
                string? version = null;
                string? changelist = null;

                try
                {
                    var manifest = ManifestLoader.Parse(File.ReadAllBytes(file.FullName));
                    var match = ManifestVersionPattern().Match(manifest.BuildVersion);
                    if (match.Success)
                    {
                        version = match.Groups[1].Value;
                        changelist = match.Groups[2].Success ? match.Groups[2].Value : null;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
                                                  or FormatException or ArgumentException or EndOfStreamException)
                {
                    continue;
                }

                entry = Store(cache, file, version, changelist);
                dirty = true;
            }

            if (entry.Version is { Length: > 0 })
                return new BuildProbe(entry.Version, entry.Changelist, "egstore manifest", file.FullName);
        }

        return null;
    }

    private static BuildProbe? ProbeExecutable(string root, ProbeCacheFile cache, ref bool dirty)
    {
        var path = Path.Combine(root, FortniteConstants.BinariesFolder, FortniteConstants.ShippingExecutable);
        var info = new FileInfo(path);
        if (!info.Exists) return null;

        var entry = Lookup(cache, info);

        if (entry is null)
        {
            (string Version, string? Changelist)? found;
            try
            {
                found = ScanExecutable(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            entry = Store(cache, info, found?.Version, found?.Changelist);
            dirty = true;
        }

        return entry.Version is { Length: > 0 }
            ? new BuildProbe(entry.Version, entry.Changelist, "executable", null)
            : null;
    }

    private static BuildProbe? ProbeBuildVersion(string root)
    {
        var path = Path.Combine(root, FortniteConstants.BuildVersionFile);
        if (!File.Exists(path)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object) return null;
            if (!json.TryGetProperty("BranchName", out var branch) || branch.ValueKind != JsonValueKind.String) return null;

            var match = ManifestVersionPattern().Match(branch.GetString() ?? string.Empty);
            if (!match.Success) return null;

            string? changelist = match.Groups[2].Success ? match.Groups[2].Value : null;
            if (json.TryGetProperty("Changelist", out var number) && number.ValueKind == JsonValueKind.Number &&
                number.TryGetInt64(out var value) && value > 0)
                changelist = value.ToString();

            return new BuildProbe(match.Groups[1].Value, changelist, "Build.version", null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static (string Version, string? Changelist)? ScanExecutable(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            BufferSize, FileOptions.SequentialScan);

        var buffer = new byte[BufferSize + Overlap];
        var kept = 0;
        (string Version, string? Changelist)? best = null;

        while (true)
        {
            var read = stream.Read(buffer, kept, BufferSize);
            var end = read == 0;
            var length = kept + read;

            foreach (var wide in Modes)
            {
                var needle = wide ? WidePrefix : AsciiPrefix;
                var span = buffer.AsSpan(0, length);
                var offset = 0;

                while (offset < length)
                {
                    var index = span[offset..].IndexOf(needle);
                    if (index < 0) break;

                    var start = offset + index;
                    offset = start + 1;

                    if (!end && start + Window > length) continue;

                    var parsed = ParseAt(buffer, start + needle.Length, length, wide);
                    if (parsed is null) continue;
                    if (parsed.Value.Changelist is not null) return parsed;
                    best ??= parsed;
                }
            }

            if (end) return best;

            kept = Math.Min(Overlap, length);
            Buffer.BlockCopy(buffer, length - kept, buffer, 0, kept);
        }
    }

    private static (string Version, string? Changelist)? ParseAt(byte[] buffer, int from, int length, bool wide)
    {
        var builder = new StringBuilder(48);
        var step = wide ? 2 : 1;

        for (var position = from; position + step <= length && builder.Length < 48; position += step)
        {
            if (wide && buffer[position + 1] != 0) break;
            var character = (char)buffer[position];
            if (!(char.IsAsciiDigit(character) || character is '.' or '-' or 'C' or 'L')) break;
            builder.Append(character);
        }

        var match = VersionPattern().Match(builder.ToString());
        if (!match.Success) return null;
        return (match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : null);
    }

    private static ProbeCacheEntry? Lookup(ProbeCacheFile cache, FileInfo file) =>
        cache.Entries.FirstOrDefault(entry =>
            entry.Key == file.FullName && entry.Length == file.Length &&
            entry.Ticks == file.LastWriteTimeUtc.Ticks);

    private static ProbeCacheEntry Store(ProbeCacheFile cache, FileInfo file, string? version, string? changelist)
    {
        cache.Entries.RemoveAll(entry => entry.Key == file.FullName);

        var created = new ProbeCacheEntry
        {
            Key = file.FullName,
            Length = file.Length,
            Ticks = file.LastWriteTimeUtc.Ticks,
            Version = version,
            Changelist = changelist
        };

        cache.Entries.Add(created);
        if (cache.Entries.Count > 200) cache.Entries.RemoveRange(0, cache.Entries.Count - 200);
        return created;
    }

    private static string CachePath(AppPaths paths) => Path.Combine(paths.Cache, "build-probes.json");

    private static ProbeCacheFile LoadCache(AppPaths paths)
    {
        try
        {
            var path = CachePath(paths);
            if (!File.Exists(path)) return new ProbeCacheFile();
            return JsonSerializer.Deserialize(File.ReadAllText(path), AppJson.Default.ProbeCacheFile) ?? new ProbeCacheFile();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ProbeCacheFile();
        }
    }

    private static void SaveCache(AppPaths paths, ProbeCacheFile cache)
    {
        try
        {
            AtomicFile.WriteAllBytes(CachePath(paths),
                JsonSerializer.SerializeToUtf8Bytes(cache, AppJson.Default.ProbeCacheFile));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
