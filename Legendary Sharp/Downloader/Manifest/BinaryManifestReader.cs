using System.IO.Compression;
using System.Security.Cryptography;

namespace Legendary_Sharp.Downloader.Manifest;

internal static class BinaryManifestReader
{
    public const uint HeaderMagic = 0x44BEC00C;

    public static bool Matches(ReadOnlySpan<byte> data) =>
        data.Length >= 4 &&
        (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24)) == HeaderMagic;

    public static BuildManifest Read(ReadOnlySpan<byte> raw)
    {
        var header = new EpicBinaryReader(raw);

        if (header.ReadUInt32() != HeaderMagic)
            throw new InvalidDataException("Not a binary Epic manifest.");

        var headerSize = (int)header.ReadUInt32();
        var uncompressedSize = (int)header.ReadUInt32();
        header.ReadUInt32();
        var shaHash = header.ReadByteArray(20);
        var storedAs = header.ReadByte();
        var version = header.ReadUInt32();

        if (version >= 22)
        {
            header.Skip(16);
            header.Skip(16);
        }

        if ((storedAs & 0x2) != 0)
            throw new NotSupportedException(
                "This manifest is encrypted and needs Epic's per-build secret, which Legendary Sharp does not have.");

        var payload = raw[headerSize..];
        byte[] body;

        if ((storedAs & 0x1) != 0)
        {
            body = Decompress(payload, uncompressedSize);
            if (!SHA1.HashData(body).AsSpan().SequenceEqual(shaHash))
                throw new InvalidDataException("Manifest payload failed its SHA-1 check, the file is corrupt.");
        }
        else
        {
            body = payload.ToArray();
        }

        return ReadBody(body);
    }

    private static byte[] Decompress(ReadOnlySpan<byte> payload, int uncompressedSize)
    {
        using var source = new MemoryStream(payload.ToArray(), writable: false);
        using var inflate = new ZLibStream(source, CompressionMode.Decompress);
        using var target = new MemoryStream(uncompressedSize > 0 ? uncompressedSize : 1 << 20);
        inflate.CopyTo(target);
        return target.ToArray();
    }

    private static BuildManifest ReadBody(ReadOnlySpan<byte> body)
    {
        var reader = new EpicBinaryReader(body);

        var metaStart = reader.Position;
        var metaSize = (int)reader.ReadUInt32();
        var dataVersion = reader.ReadByte();
        var featureLevel = (int)reader.ReadUInt32();
        reader.ReadByte();
        reader.ReadUInt32();

        var appName = reader.ReadString();
        var buildVersion = reader.ReadString();
        var launchExe = reader.ReadString();
        var launchCommand = reader.ReadString();

        var prerequisiteCount = (int)reader.ReadUInt32();
        for (var i = 0; i < prerequisiteCount; i++) reader.ReadString();

        reader.ReadString();
        reader.ReadString();
        reader.ReadString();

        var buildId = dataVersion >= 1 ? reader.ReadString() : string.Empty;
        reader.Seek(metaStart + metaSize);

        var chunks = ReadChunkList(ref reader, featureLevel);
        var files = ReadFileList(ref reader);
        var customFields = ReadCustomFields(ref reader);

        return new BuildManifest(featureLevel, appName, buildVersion, launchExe, launchCommand, buildId,
            chunks, files, customFields, ManifestFormat.Binary);
    }

    private static ChunkInfo[] ReadChunkList(ref EpicBinaryReader reader, int featureLevel)
    {
        var start = reader.Position;
        var size = (int)reader.ReadUInt32();
        reader.ReadByte();
        var count = (int)reader.ReadUInt32();

        var guids = new ChunkGuid[count];
        var hashes = new ulong[count];
        var shaHashes = new byte[count][];
        var groups = new byte[count];
        var windows = new uint[count];
        var sizes = new long[count];

        for (var i = 0; i < count; i++) guids[i] = reader.ReadGuid();
        for (var i = 0; i < count; i++) hashes[i] = reader.ReadUInt64();
        for (var i = 0; i < count; i++) shaHashes[i] = reader.ReadByteArray(20);
        for (var i = 0; i < count; i++) groups[i] = reader.ReadByte();
        for (var i = 0; i < count; i++) windows[i] = reader.ReadUInt32();
        for (var i = 0; i < count; i++) sizes[i] = reader.ReadInt64();

        var chunks = new ChunkInfo[count];
        for (var i = 0; i < count; i++)
            chunks[i] = new ChunkInfo
            {
                Guid = guids[i],
                Hash = hashes[i],
                ShaHash = shaHashes[i],
                GroupNumber = groups[i],
                WindowSize = windows[i],
                CompressedSize = sizes[i]
            };

        reader.Seek(start + size);
        return chunks;
    }

    private static FileEntry[] ReadFileList(ref EpicBinaryReader reader)
    {
        var start = reader.Position;
        var size = (int)reader.ReadUInt32();
        var version = reader.ReadByte();
        var count = (int)reader.ReadUInt32();

        var names = new string[count];
        var symlinks = new string[count];
        var hashes = new byte[count][];
        var flags = new byte[count];
        var tags = new string[count][];
        var parts = new ChunkPart[count][];
        var fileSizes = new long[count];

        for (var i = 0; i < count; i++) names[i] = reader.ReadString();
        for (var i = 0; i < count; i++) symlinks[i] = reader.ReadString();
        for (var i = 0; i < count; i++) hashes[i] = reader.ReadByteArray(20);
        for (var i = 0; i < count; i++) flags[i] = reader.ReadByte();

        for (var i = 0; i < count; i++)
        {
            var tagCount = (int)reader.ReadUInt32();
            if (tagCount == 0)
            {
                tags[i] = [];
                continue;
            }

            var list = new string[tagCount];
            for (var t = 0; t < tagCount; t++) list[t] = reader.ReadString();
            tags[i] = list;
        }

        for (var i = 0; i < count; i++)
        {
            var partCount = (int)reader.ReadUInt32();
            var list = new ChunkPart[partCount];
            long total = 0;

            for (var p = 0; p < partCount; p++)
            {
                var partStart = reader.Position;
                var partSize = (int)reader.ReadUInt32();
                var guid = reader.ReadGuid();
                var offset = reader.ReadUInt32();
                var length = reader.ReadUInt32();

                list[p] = new ChunkPart { Guid = guid, Offset = offset, Size = length };
                total += length;
                reader.Seek(partStart + partSize);
            }

            parts[i] = list;
            fileSizes[i] = total;
        }

        if (version >= 1)
        {
            for (var i = 0; i < count; i++)
                if (reader.ReadUInt32() != 0)
                    reader.Skip(16);

            for (var i = 0; i < count; i++) reader.ReadString();
        }

        if (version >= 2)
            for (var i = 0; i < count; i++)
                reader.Skip(32);

        var files = new FileEntry[count];
        for (var i = 0; i < count; i++)
            files[i] = new FileEntry
            {
                FileName = names[i],
                SymlinkTarget = symlinks[i],
                ShaHash = hashes[i],
                Flags = flags[i],
                InstallTags = tags[i],
                Parts = parts[i],
                Size = fileSizes[i]
            };

        reader.Seek(start + size);
        return files;
    }

    private static Dictionary<string, string> ReadCustomFields(ref EpicBinaryReader reader)
    {
        if (reader.Position >= reader.Length) return [];

        var start = reader.Position;
        var size = (int)reader.ReadUInt32();
        reader.ReadByte();
        var count = (int)reader.ReadUInt32();

        var keys = new string[count];
        for (var i = 0; i < count; i++) keys[i] = reader.ReadString();

        var fields = new Dictionary<string, string>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++) fields[keys[i]] = reader.ReadString();

        reader.Seek(start + size);
        return fields;
    }
}
