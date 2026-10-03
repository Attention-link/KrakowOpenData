using System.IO.Compression;
using System.Text;
using KrakowOpenData.Application.Abstractions;

namespace KrakowOpenData.Infrastructure.Tests;

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public void Advance(TimeSpan by) => UtcNow += by;
}

public static class ZipBuilder
{
    /// <summary>Builds an in-memory zip from (file name, content) pairs.</summary>
    public static MemoryStream Build(params (string Name, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}

/// <summary>
/// Minimal protobuf writer used only to build GTFS-Realtime test fixtures, so the decoder is
/// tested against bytes laid out exactly as gtfs-realtime.proto specifies.
/// </summary>
public sealed class ProtoWriter
{
    private readonly MemoryStream _stream = new();

    public byte[] ToArray() => _stream.ToArray();

    public ProtoWriter Varint(int field, long value)
    {
        Tag(field, 0);
        WriteVarint(unchecked((ulong)value));
        return this;
    }

    public ProtoWriter String(int field, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Tag(field, 2);
        WriteVarint((ulong)bytes.Length);
        _stream.Write(bytes);
        return this;
    }

    public ProtoWriter Float(int field, float value)
    {
        Tag(field, 5);
        Span<byte> buffer = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        _stream.Write(buffer);
        return this;
    }

    public ProtoWriter Fixed64(int field, double value)
    {
        Tag(field, 1);
        Span<byte> buffer = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        _stream.Write(buffer);
        return this;
    }

    public ProtoWriter Message(int field, Action<ProtoWriter> build)
    {
        var inner = new ProtoWriter();
        build(inner);
        var bytes = inner.ToArray();
        Tag(field, 2);
        WriteVarint((ulong)bytes.Length);
        _stream.Write(bytes);
        return this;
    }

    private void Tag(int field, int wireType) => WriteVarint((ulong)((field << 3) | wireType));

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        _stream.WriteByte((byte)value);
    }
}
