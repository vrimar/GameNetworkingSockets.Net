using System.Buffers.Binary;

namespace Valve.Sockets;

internal ref struct ProtoWriter(Span<byte> buffer)
{
    private readonly Span<byte> _buffer = buffer;
    private int _length;

    public readonly ReadOnlySpan<byte> Written => _buffer[.._length];

    public void Varint(int field, ulong value)
    {
        Tag(field, ProtoReader.VarintWire);
        WriteVarint(value);
    }

    public void Fixed32(int field, uint value)
    {
        Tag(field, ProtoReader.Fixed32Wire);
        BinaryPrimitives.WriteUInt32LittleEndian(Take(4), value);
    }

    public void Fixed64(int field, ulong value)
    {
        Tag(field, ProtoReader.Fixed64Wire);
        BinaryPrimitives.WriteUInt64LittleEndian(Take(8), value);
    }

    public void Bytes(int field, ReadOnlySpan<byte> value)
    {
        Tag(field, ProtoReader.LengthDelimited);
        WriteVarint((ulong)value.Length);
        value.CopyTo(Take(value.Length));
    }

    private void Tag(int field, int wireType) => WriteVarint((ulong)((field << 3) | wireType));

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            Take(1)[0] = (byte)(value | 0x80);
            value >>= 7;
        }

        Take(1)[0] = (byte)value;
    }

    private Span<byte> Take(int count)
    {
        var taken = _buffer.Slice(_length, count);
        _length += count;
        return taken;
    }
}
