using System.Globalization;

namespace Legendary_Sharp.Downloader.Manifest;

internal readonly struct ChunkGuid(uint a, uint b, uint c, uint d) : IEquatable<ChunkGuid>
{
    public readonly uint A = a;
    public readonly uint B = b;
    public readonly uint C = c;
    public readonly uint D = d;

    public static ChunkGuid ParseHex(ReadOnlySpan<char> hex)
    {
        if (hex.Length != 32) throw new FormatException($"Expected a 32 character GUID but got \"{hex}\".");

        return new ChunkGuid(
            uint.Parse(hex[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            uint.Parse(hex.Slice(8, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            uint.Parse(hex.Slice(16, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            uint.Parse(hex.Slice(24, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public string ToHex() => string.Create(32, this, static (span, guid) =>
    {
        guid.A.TryFormat(span, out _, "X8", CultureInfo.InvariantCulture);
        guid.B.TryFormat(span[8..], out _, "X8", CultureInfo.InvariantCulture);
        guid.C.TryFormat(span[16..], out _, "X8", CultureInfo.InvariantCulture);
        guid.D.TryFormat(span[24..], out _, "X8", CultureInfo.InvariantCulture);
    });

    public bool Equals(ChunkGuid other) => A == other.A && B == other.B && C == other.C && D == other.D;

    public override bool Equals(object? obj) => obj is ChunkGuid other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(A, B, C, D);

    public override string ToString() => ToHex();
}
