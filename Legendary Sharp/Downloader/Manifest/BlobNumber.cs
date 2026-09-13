namespace Legendary_Sharp.Downloader.Manifest;

internal static class BlobNumber
{
    public static ulong ToUInt64(ReadOnlySpan<char> blob)
    {
        ulong value = 0;
        var shift = 0;

        for (var i = 0; i + 3 <= blob.Length && shift < 64; i += 3, shift += 8)
            value |= (ulong)ReadTriplet(blob.Slice(i, 3)) << shift;

        return value;
    }

    public static uint ToUInt32(ReadOnlySpan<char> blob) => (uint)ToUInt64(blob);

    public static byte[] ToBytes(ReadOnlySpan<char> blob)
    {
        if (blob.Length % 3 != 0)
            throw new FormatException($"Blob number \"{blob}\" has an unexpected length.");

        var bytes = new byte[blob.Length / 3];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = ReadTriplet(blob.Slice(i * 3, 3));
        return bytes;
    }

    private static byte ReadTriplet(ReadOnlySpan<char> triplet)
    {
        var value = 0;
        foreach (var character in triplet)
        {
            if (character is < '0' or > '9')
                throw new FormatException($"Blob number contains a non digit character '{character}'.");
            value = value * 10 + (character - '0');
        }

        if (value > byte.MaxValue)
            throw new FormatException($"Blob number triplet \"{triplet}\" is out of range.");

        return (byte)value;
    }
}
