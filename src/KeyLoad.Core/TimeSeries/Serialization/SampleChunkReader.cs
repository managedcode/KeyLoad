using System.Buffers.Binary;

namespace KeyLoad.Core.Features.TimeSeries;

internal ref struct SampleChunkReader
{
    private const int EmptyRemainingBytes = 0;

    private readonly ReadOnlySpan<byte> source;
    private int position;

    internal SampleChunkReader(ReadOnlySpan<byte> source)
    {
        const int PositionEmptyCount = 0;

        this.source = source;
        position = PositionEmptyCount;
    }

    internal readonly int Remaining => source.Length - position;

    internal byte ReadByte()
    {
        const int EmptyRemainingBytes = 0;

        SampleChunkWire.Require(Remaining > EmptyRemainingBytes);
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
        const int EmptySpanLength = 0;

        SampleChunkWire.Require(length >= EmptySpanLength && length <= Remaining);
        var value = source.Slice(position, length);
        position += length;
        return value;
    }

    internal ulong ReadVarUInt()
    {
        const int ValueInitialValue = 0;
        const int ShiftInitialValue = 0;
        const int LengthInitialValue = 0;
        const int MaximumVarUInt64Bytes = 10;
        const int LastUInt64PayloadBit = 63;
        const int LastByteOverflowMask = 0xFE;
        const int ClearLastByteOverflowBits = 0;
        const int VarUIntPayloadMask = 0x7F;
        const int VarUIntPayloadBits = 7;
        const int VarUIntContinuationMask = 0x80;
        const int FinalByteContinuationFlag = 0;

        ulong value = ValueInitialValue;
        var shift = ShiftInitialValue;
        var length = LengthInitialValue;
        byte next;
        do
        {
            SampleChunkWire.Require(length < MaximumVarUInt64Bytes);
            next = ReadByte();
            if (shift == LastUInt64PayloadBit)
            {
                SampleChunkWire.Require((next & LastByteOverflowMask) == ClearLastByteOverflowBits);
            }
            value |= (ulong)(next & VarUIntPayloadMask) << shift;
            shift += VarUIntPayloadBits;
            length++;
        } while ((next & VarUIntContinuationMask) != FinalByteContinuationFlag);
        SampleChunkWire.Require(length == SampleChunkWire.VarUIntLength(value));
        return value;
    }

    internal readonly void RequireEnd() => SampleChunkWire.Require(Remaining == EmptyRemainingBytes);
}
