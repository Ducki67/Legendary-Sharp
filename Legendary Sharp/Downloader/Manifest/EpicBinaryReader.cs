using System.Buffers.Binary;
using System.Text;

namespace Legendary_Sharp.Downloader.Manifest;

internal ref struct EpicBinaryReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;

    public int Position { get; private set; }

    public readonly int Length => _data.Length;

    public void Seek(int position)
    {
        if (position < 0 || position > _data.Length)
            throw new InvalidDataException($"Manifest offset {position} is outside the data.");
        Position = position;
    }

    public void Skip(int count) => Seek(Position + count);

    public byte ReadByte()
    {
        Require(1);
        return _data[Position++];
    }

    public uint ReadUInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data[Position..]);
        Position += 4;
        return value;
    }

    public int ReadInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadInt32LittleEndian(_data[Position..]);
        Position += 4;
        return value;
    }

    public ulong ReadUInt64()
    {
        Require(8);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(_data[Position..]);
        Position += 8;
        return value;
    }

    public long ReadInt64()
    {
        Require(8);
        var value = BinaryPrimitives.ReadInt64LittleEndian(_data[Position..]);
        Position += 8;
        return value;
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        Require(count);
        var slice = _data.Slice(Position, count);
        Position += count;
        return slice;
    }

    public byte[] ReadByteArray(int count) => ReadBytes(count).ToArray();

    public ChunkGuid ReadGuid() => new(ReadUInt32(), ReadUInt32(), ReadUInt32(), ReadUInt32());

    public string ReadString()
    {
        var length = ReadInt32();

        switch (length)
        {
            case 0:
                return string.Empty;
            case < 0:
            {
                var bytes = ReadBytes(-length * 2 - 2);
                Skip(2);
                return Encoding.Unicode.GetString(bytes);
            }
            default:
            {
                var bytes = ReadBytes(length - 1);
                Skip(1);
                return Encoding.Latin1.GetString(bytes);
            }
        }
    }

    private readonly void Require(int count)
    {
        if (count < 0 || Position + count > _data.Length)
            throw new InvalidDataException("Manifest data ended unexpectedly.");
    }
}
