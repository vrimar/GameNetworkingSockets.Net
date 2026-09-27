using System.Buffers.Binary;

namespace Valve.Sockets;

internal ref struct ProtoReader(ReadOnlySpan<byte> data)
{
    public const int VarintWire = 0;
    public const int Fixed64Wire = 1;
    public const int LengthDelimited = 2;
    public const int Fixed32Wire = 5;

    private ReadOnlySpan<byte> _data = data;

    public bool Next(out int field, out int wireType)
    {
        if (_data.IsEmpty)
        {
            field = wireType = 0;
            return false;
        }

        var tag = Varint();
        field = (int)(tag >> 3);
        wireType = (int)(tag & 7);
        return true;
    }

    public ulong Varint()
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var b = Take(1)[0];
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
                return value;
        }

        throw new FormatException("Malformed certificate: a varint runs past 64 bits.");
    }

    public uint Fixed32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public ulong Fixed64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    public ReadOnlySpan<byte> Bytes()
    {
        var length = Varint();
        return Take(length > (ulong)_data.Length ? int.MaxValue : (int)length);
    }

    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case VarintWire:
                Varint();
                break;
            case Fixed64Wire:
                Take(8);
                break;
            case LengthDelimited:
                Bytes();
                break;
            case Fixed32Wire:
                Take(4);
                break;
            default:
                throw new FormatException($"Malformed certificate: unknown wire type {wireType}.");
        }
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count > _data.Length)
            throw new FormatException("Malformed certificate: it ends mid-field.");

        var taken = _data[..count];
        _data = _data[count..];
        return taken;
    }
}
