using System.Buffers.Binary;
using System.Text;

namespace KrakowOpenData.Infrastructure.GtfsRealtime;

/// <summary>
/// A tiny protocol-buffers wire-format reader, enough to decode GTFS-Realtime without a NuGet
/// dependency. Supports varint, 64-bit, length-delimited and 32-bit wire types; unknown fields
/// are skipped, which keeps the decoder forward compatible with feed extensions.
/// </summary>
public sealed class ProtoReader
{
    public const int WireVarint = 0;
    public const int WireFixed64 = 1;
    public const int WireLengthDelimited = 2;
    public const int WireFixed32 = 5;

    private readonly byte[] _buffer;
    private readonly int _end;
    private int _position;

    public ProtoReader(byte[] buffer) : this(buffer, 0, buffer.Length)
    {
    }

    private ProtoReader(byte[] buffer, int start, int end)
    {
        _buffer = buffer;
        _position = start;
        _end = end;
    }

    public bool IsAtEnd => _position >= _end;

    public bool TryReadTag(out int fieldNumber, out int wireType)
    {
        if (IsAtEnd)
        {
            fieldNumber = 0;
            wireType = 0;
            return false;
        }

        var tag = ReadVarint();
        fieldNumber = (int)(tag >> 3);
        wireType = (int)(tag & 0x7);
        if (fieldNumber <= 0) throw new InvalidDataException("Invalid protobuf field number.");
        return true;
    }

    public ulong ReadVarint()
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            EnsureAvailable(1);
            var b = _buffer[_position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }

        throw new InvalidDataException("Malformed protobuf varint.");
    }

    public bool ReadBool() => ReadVarint() != 0;

    /// <summary>int32/int64/enum fields: negative values are sign-extended 10-byte varints.</summary>
    public long ReadInt64() => unchecked((long)ReadVarint());

    public int ReadInt32() => unchecked((int)ReadInt64());

    public uint ReadUInt32() => unchecked((uint)ReadVarint());

    public ulong ReadUInt64() => ReadVarint();

    public float ReadFloat()
    {
        EnsureAvailable(4);
        var value = BinaryPrimitives.ReadSingleLittleEndian(_buffer.AsSpan(_position, 4));
        _position += 4;
        return value;
    }

    public double ReadDouble()
    {
        EnsureAvailable(8);
        var value = BinaryPrimitives.ReadDoubleLittleEndian(_buffer.AsSpan(_position, 8));
        _position += 8;
        return value;
    }

    public string ReadString()
    {
        var length = ReadLength();
        var value = Encoding.UTF8.GetString(_buffer, _position, length);
        _position += length;
        return value;
    }

    /// <summary>Returns a reader over an embedded message and advances past it.</summary>
    public ProtoReader ReadMessage()
    {
        var length = ReadLength();
        var sub = new ProtoReader(_buffer, _position, _position + length);
        _position += length;
        return sub;
    }

    public void SkipField(int wireType)
    {
        switch (wireType)
        {
            case WireVarint:
                ReadVarint();
                break;
            case WireFixed64:
                EnsureAvailable(8);
                _position += 8;
                break;
            case WireLengthDelimited:
                var length = ReadLength();
                _position += length;
                break;
            case WireFixed32:
                EnsureAvailable(4);
                _position += 4;
                break;
            default:
                throw new InvalidDataException($"Unsupported protobuf wire type {wireType}.");
        }
    }

    private int ReadLength()
    {
        var length = ReadVarint();
        if (length > int.MaxValue) throw new InvalidDataException("Protobuf length too large.");
        EnsureAvailable((int)length);
        return (int)length;
    }

    private void EnsureAvailable(int count)
    {
        if (count < 0 || _position + count > _end)
            throw new InvalidDataException("Unexpected end of protobuf data.");
    }
}
