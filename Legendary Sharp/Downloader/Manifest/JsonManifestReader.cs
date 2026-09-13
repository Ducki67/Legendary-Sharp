using System.Text.Json;

namespace Legendary_Sharp.Downloader.Manifest;

internal static class JsonManifestReader
{
    public static bool Matches(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0xEF or 0xBB or 0xBF) continue;
            return value == (byte)'{';
        }

        return false;
    }

    public static BuildManifest Read(ReadOnlySpan<byte> raw)
    {
        using var document = JsonDocument.Parse(raw.ToArray());
        var root = document.RootElement;

        var featureLevel = (int)BlobNumber.ToUInt64(Text(root, "ManifestFileVersion", "013000000000"));
        var chunks = ReadChunks(root);
        var files = ReadFiles(root);
        var customFields = ReadCustomFields(root);

        return new BuildManifest(
            featureLevel,
            Text(root, "AppNameString", string.Empty),
            Text(root, "BuildVersionString", string.Empty),
            Text(root, "LaunchExeString", string.Empty),
            Text(root, "LaunchCommand", string.Empty),
            string.Empty,
            chunks,
            files,
            customFields,
            ManifestFormat.Json);
    }

    private static ChunkInfo[] ReadChunks(JsonElement root)
    {
        var sizes = Require(root, "ChunkFilesizeList");
        var hashes = Require(root, "ChunkHashList");
        var shaHashes = Require(root, "ChunkShaList");
        var groups = Require(root, "DataGroupList");

        var chunks = new List<ChunkInfo>(sizes.GetRawText().Length / 64);

        foreach (var entry in sizes.EnumerateObject())
        {
            var guid = ChunkGuid.ParseHex(entry.Name);

            chunks.Add(new ChunkInfo
            {
                Guid = guid,
                CompressedSize = (long)BlobNumber.ToUInt64(entry.Value.GetString() ?? string.Empty),
                Hash = BlobNumber.ToUInt64(Lookup(hashes, entry.Name)),
                ShaHash = Convert.FromHexString(Lookup(shaHashes, entry.Name)),
                GroupNumber = (byte)BlobNumber.ToUInt64(Lookup(groups, entry.Name)),
                WindowSize = 1024 * 1024
            });
        }

        return [.. chunks];
    }

    private static FileEntry[] ReadFiles(JsonElement root)
    {
        var list = Require(root, "FileManifestList");
        var files = new List<FileEntry>(list.GetArrayLength());

        foreach (var element in list.EnumerateArray())
        {
            var parts = new List<ChunkPart>();
            long size = 0;

            if (element.TryGetProperty("FileChunkParts", out var chunkParts))
                foreach (var part in chunkParts.EnumerateArray())
                {
                    var length = BlobNumber.ToUInt32(part.GetProperty("Size").GetString() ?? string.Empty);

                    parts.Add(new ChunkPart
                    {
                        Guid = ChunkGuid.ParseHex(part.GetProperty("Guid").GetString() ?? string.Empty),
                        Offset = BlobNumber.ToUInt32(part.GetProperty("Offset").GetString() ?? string.Empty),
                        Size = length
                    });

                    size += length;
                }

            byte flags = 0;
            if (Flag(element, "bIsReadOnly")) flags |= 0x1;
            if (Flag(element, "bIsCompressed")) flags |= 0x2;
            if (Flag(element, "bIsUnixExecutable")) flags |= 0x4;

            var tags = Array.Empty<string>();
            if (element.TryGetProperty("InstallTags", out var tagList) && tagList.ValueKind == JsonValueKind.Array)
                tags = [.. tagList.EnumerateArray().Select(tag => tag.GetString() ?? string.Empty)];

            files.Add(new FileEntry
            {
                FileName = element.GetProperty("Filename").GetString() ?? string.Empty,
                SymlinkTarget = Text(element, "SymlinkTarget", string.Empty),
                ShaHash = BlobNumber.ToBytes(Text(element, "FileHash", string.Empty)),
                Flags = flags,
                InstallTags = tags,
                Parts = [.. parts],
                Size = size
            });
        }

        return [.. files];
    }

    private static Dictionary<string, string> ReadCustomFields(JsonElement root)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("CustomFields", out var element) || element.ValueKind != JsonValueKind.Object)
            return fields;

        foreach (var entry in element.EnumerateObject())
            fields[entry.Name] = entry.Value.ValueKind == JsonValueKind.String
                ? entry.Value.GetString() ?? string.Empty
                : entry.Value.GetRawText();

        return fields;
    }

    private static JsonElement Require(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element)
            ? element
            : throw new InvalidDataException($"JSON manifest is missing \"{name}\".");

    private static string Lookup(JsonElement map, string key) =>
        map.TryGetProperty(key, out var element)
            ? element.GetString() ?? string.Empty
            : throw new InvalidDataException($"JSON manifest has no entry for chunk {key}.");

    private static string Text(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
