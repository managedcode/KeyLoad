using System.Buffers.Binary;

namespace KeyLoad.Orleans.Features.ResourceExecution;

internal ref struct CacheControlWriter(Span<byte> destination, bool measureOnly = false)
{
    private readonly Span<byte> buffer = destination;
    private readonly bool sizing = measureOnly;
    internal int Position { get; private set; }

    internal void Id(ushort id)
    {
        var offset = Advance(sizeof(ushort));
        if (!sizing)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], id);
        }
    }

    internal void Byte(byte value)
    {
        var offset = Advance(sizeof(byte));
        if (!sizing)
        {
            buffer[offset] = value;
        }
    }

    internal void UInt32(uint value)
    {
        var offset = Advance(sizeof(uint));
        if (!sizing)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], value);
        }
    }

    internal void ByteField(ushort id, byte value)
    {
        Id(id);
        Byte(value);
    }

    internal void Int64Field(ushort id, long value)
    {
        Id(id);
        var offset = Advance(sizeof(long));
        if (!sizing)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer[offset..], value);
        }
    }

    internal void GuidField(ushort id, Guid value)
    {
        Id(id);
        var offset = Advance(16);
        if (!sizing)
        {
            _ = value.TryWriteBytes(buffer.Slice(offset, 16), bigEndian: true, out _);
        }
    }

    internal void DigestField(ushort id, CacheControlDigest value)
    {
        Id(id);
        Digest(value);
    }

    internal void Digest(CacheControlDigest value)
    {
        var offset = Advance(CacheControlDigest.ByteLength);
        if (!sizing)
        {
            value.WriteBytes(buffer.Slice(offset, CacheControlDigest.ByteLength));
        }
    }

    internal void String(string value)
    {
        var count = CacheControlValidation.StrictUtf8.GetByteCount(value);
        UInt32(checked((uint)count));
        var offset = Advance(count);
        if (!sizing)
        {
            _ = CacheControlValidation.StrictUtf8.GetBytes(value, buffer[offset..]);
        }
    }

    internal void StringField(ushort id, string value)
    {
        Id(id);
        String(value);
    }

    internal void NullableStringField(ushort id, string? value)
    {
        Id(id);
        Byte(value is null ? (byte)0 : (byte)1);
        if (value is not null)
        {
            String(value);
        }
    }

    internal void NullableSlotField(ushort id, CacheVoterSlot? value)
    {
        Id(id);
        Byte(value.HasValue ? (byte)1 : (byte)0);
        if (value is { } slot)
        {
            Byte((byte)slot);
        }
    }

    internal void Bytes(scoped ReadOnlySpan<byte> value)
    {
        var offset = Advance(value.Length);
        if (!sizing)
        {
            value.CopyTo(buffer[offset..]);
        }
    }

    private int Advance(int count)
    {
        var offset = Position;
        Position = checked(Position + count);
        return offset;
    }
}
