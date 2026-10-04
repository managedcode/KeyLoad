using System.Buffers.Binary;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWire
{
    internal const string PayloadAlias = "keyload.core.v1.SampleChunkPayload";
    internal const int CurrentVersion = 1;
    internal const int MaximumRecords = 256;
    internal const int MaximumEncodedBytes = 8_388_608;
    internal const int ChecksumBytes = 32;
    internal const int HashChunkBytes = 65_536;
    internal const string InvalidMaximum = "The sample chunk byte limit is outside its supported bounds.";
    internal const string InvalidContent = "The sample chunk contains invalid sample content.";
    internal const string InvalidShape = "The sample chunk payload is malformed.";
    internal const string UnsupportedVersion = "The sample chunk format is unsupported.";
    internal const string ExcessRecords = "The sample chunk record limit is exceeded.";
    internal const string ExcessBytes = "The sample chunk byte limit is exceeded.";

    internal static void ValidateMaximum(int maximumBytes)
    {
        if (maximumBytes is < 1 or > MaximumEncodedBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidMaximum);
        }
    }

    internal static int VarUIntLength(ulong value)
    {
        var length = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            length++;
        }
        return length;
    }

    internal static void WriteVarUInt(Span<byte> destination, ref int position, ulong value)
    {
        while (value >= 0x80)
        {
            destination[position++] = (byte)(value | 0x80);
            value >>= 7;
        }
        destination[position++] = (byte)value;
    }

    internal static ulong ZigZag(long value)
        => unchecked((ulong)((value << 1) ^ (value >> 63)));

    internal static long UnZigZag(ulong value)
        => unchecked((long)(value >> 1) ^ -((long)value & 1));

    internal static void Require(bool condition)
    {
        if (!condition)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidShape);
        }
    }

    internal static void AddSize(ref long total, long amount)
    {
        if (amount < 0 || amount > MaximumEncodedBytes - total)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ExcessBytes);
        }
        total += amount;
    }
}

internal ref struct SampleChunkReader
{
    private readonly ReadOnlySpan<byte> source;
    private int position;

    internal SampleChunkReader(ReadOnlySpan<byte> source)
    {
        this.source = source;
        position = 0;
    }

    internal readonly int Remaining => source.Length - position;

    internal byte ReadByte()
    {
        SampleChunkWire.Require(Remaining > 0);
        return source[position++];
    }

    internal short ReadInt16LittleEndian()
    {
        SampleChunkWire.Require(Remaining >= sizeof(short));
        var value = BinaryPrimitives.ReadInt16LittleEndian(source[position..]);
        position += sizeof(short);
        return value;
    }

    internal ReadOnlySpan<byte> ReadSpan(int length)
    {
        SampleChunkWire.Require(length >= 0 && length <= Remaining);
        var value = source.Slice(position, length);
        position += length;
        return value;
    }

    internal ulong ReadVarUInt()
    {
        ulong value = 0;
        var shift = 0;
        var length = 0;
        byte next;
        do
        {
            SampleChunkWire.Require(length < 10);
            next = ReadByte();
            if (shift == 63)
            {
                SampleChunkWire.Require((next & 0xFE) == 0);
            }
            value |= (ulong)(next & 0x7F) << shift;
            shift += 7;
            length++;
        } while ((next & 0x80) != 0);
        SampleChunkWire.Require(length == SampleChunkWire.VarUIntLength(value));
        return value;
    }

    internal readonly void RequireEnd() => SampleChunkWire.Require(Remaining == 0);
}
