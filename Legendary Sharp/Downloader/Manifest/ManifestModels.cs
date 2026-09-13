using System.Globalization;

namespace Legendary_Sharp.Downloader.Manifest;

internal sealed class ChunkInfo
{
    public ChunkGuid Guid { get; init; }

    public ulong Hash { get; init; }

    public byte[] ShaHash { get; init; } = [];

    public byte GroupNumber { get; init; }

    public uint WindowSize { get; init; }

    public long CompressedSize { get; init; }

    public string BuildPath(int featureLevel) => string.Create(
        CultureInfo.InvariantCulture,
        $"{ChunkDirectory(featureLevel)}/{GroupNumber:D2}/{Hash:X16}_{Guid.ToHex()}.chunk");

    private static string ChunkDirectory(int featureLevel) => featureLevel switch
    {
        >= 22 => "ChunksV5",
        >= 15 => "ChunksV4",
        >= 6 => "ChunksV3",
        >= 3 => "ChunksV2",
        _ => "Chunks"
    };
}

internal sealed class ChunkPart
{
    public ChunkGuid Guid { get; init; }

    public uint Offset { get; init; }

    public uint Size { get; init; }
}

internal sealed class FileEntry
{
    public string FileName { get; init; } = string.Empty;

    public string SymlinkTarget { get; init; } = string.Empty;

    public byte[] ShaHash { get; init; } = [];

    public byte Flags { get; init; }

    public string[] InstallTags { get; init; } = [];

    public ChunkPart[] Parts { get; init; } = [];

    public long Size { get; init; }

    public bool IsReadOnly => (Flags & 0x1) != 0;

    public bool IsExecutable => (Flags & 0x4) != 0;

    public string ToNativePath() => FileName.Replace('/', Path.DirectorySeparatorChar);
}
