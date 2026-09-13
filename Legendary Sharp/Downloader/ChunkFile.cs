using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal static class ChunkFile
{
    private const uint HeaderMagic = 0xB1FE3AA2;
    private const byte StoredCompressed = 0x1;
    private const byte StoredEncrypted = 0x2;
    private const byte HashTypeSha = 0x2;

    public static byte[] Unpack(ReadOnlySpan<byte> raw, ChunkInfo expected, bool verify)
    {
        if (raw.Length < 41) throw new InvalidDataException("Chunk response is too small to be a chunk.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(raw) != HeaderMagic)
            throw new InvalidDataException("Chunk magic does not match, the CDN returned something else.");

        var headerVersion = BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]);
        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(raw[8..]);
        var compressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(raw[12..]);
        var storedAs = raw[40];

        byte[]? shaHash = null;
        byte hashType = 0;
        var uncompressedSize = 1024 * 1024;

        var offset = 41;
        if (headerVersion >= 2)
        {
            shaHash = raw.Slice(offset, 20).ToArray();
            hashType = raw[offset + 20];
            offset += 21;
        }

        if (headerVersion >= 3)
        {
            uncompressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
            offset += 4;
        }

        if ((storedAs & StoredEncrypted) != 0)
            throw new NotSupportedException("Chunk is encrypted and cannot be unpacked without Epic's secret.");

        if (headerSize < offset || headerSize > raw.Length)
            throw new InvalidDataException("Chunk header size is out of range.");

        var payload = raw[headerSize..];
        if (compressedSize > 0 && payload.Length > compressedSize) payload = payload[..compressedSize];

        var data = (storedAs & StoredCompressed) != 0
            ? Inflate(payload, uncompressedSize)
            : payload.ToArray();

        if (!verify) return data;

        if (shaHash is not null && (hashType & HashTypeSha) != 0)
        {
            if (!SHA1.HashData(data).AsSpan().SequenceEqual(shaHash))
                throw new InvalidDataException("Chunk failed its SHA-1 check.");
        }
        else if (expected.ShaHash.Length == 20)
        {
            if (!SHA1.HashData(data).AsSpan().SequenceEqual(expected.ShaHash))
                throw new InvalidDataException("Chunk failed the manifest SHA-1 check.");
        }

        return data;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> payload, int uncompressedSize)
    {
        using var source = new MemoryStream(payload.ToArray(), writable: false);
        using var inflate = new ZLibStream(source, CompressionMode.Decompress);

        if (uncompressedSize > 0)
        {
            var buffer = new byte[uncompressedSize];
            inflate.ReadExactly(buffer);
            return buffer;
        }

        using var target = new MemoryStream(1024 * 1024);
        inflate.CopyTo(target);
        return target.ToArray();
    }
}
