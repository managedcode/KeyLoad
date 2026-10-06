using System.Buffers;
using System.Text;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkText
{
    private const byte Utf8Encoding = 0;
    private const byte Utf16Encoding = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static long FramedSize(string value, ReadExecutionBudget budget, int textCancellationCheckIntervalCodeUnits)
    {
        const int EncodingFlagBits = 1;

        var byteLength = MeasureText(value, budget, textCancellationCheckIntervalCodeUnits, out var encoding);
        var prefix = checked(((ulong)byteLength << EncodingFlagBits) | encoding);
        return SampleChunkWire.VarUIntLength(prefix) + byteLength;
    }

    internal static void WriteFramed(string value, Span<byte> destination, ref int position,
        ReadExecutionBudget budget, int hashChunkBytes, int textCancellationCheckIntervalCodeUnits)
    {
        const int EncodingFlagBits = 1;

        var byteLength = MeasureText(value, budget, textCancellationCheckIntervalCodeUnits, out var encoding);
        var prefix = checked(((ulong)byteLength << EncodingFlagBits) | encoding);
        SampleChunkWire.WriteVarUInt(destination, ref position, prefix);
        var target = destination.Slice(position, byteLength);
        budget.Check();
        if (encoding == Utf16Encoding)
        {
            SampleChunkUtf16.Write(value, target, budget, hashChunkBytes);
        }
        else
        {
            _ = StrictUtf8.GetBytes(value.AsSpan(), target);
            budget.Check();
        }
        position += byteLength;
    }

    internal static long ValidateFramed(ref SampleChunkReader reader, ReadExecutionBudget budget, int hashChunkBytes)
    {
        const int EncodingFlagMask = 1;
        const int PrefixBitOffset = 1;

        var prefix = reader.ReadVarUInt();
        var encoding = (byte)(prefix & EncodingFlagMask);
        var byteLength = prefix >> PrefixBitOffset;
        SampleChunkWire.Require(byteLength <= int.MaxValue);
        var bytes = reader.ReadSpan((int)byteLength);
        ValidateBytes(bytes, encoding, budget, hashChunkBytes);
        return checked((long)byteLength);
    }

    internal static string ReadFramed(ref SampleChunkReader reader, ReadExecutionBudget budget, int hashChunkBytes)
    {
        const int EncodingFlagMask = 1;
        const int PrefixBitOffset = 1;

        var prefix = reader.ReadVarUInt();
        var encoding = (byte)(prefix & EncodingFlagMask);
        var byteLength = prefix >> PrefixBitOffset;
        SampleChunkWire.Require(byteLength <= int.MaxValue);
        var bytes = reader.ReadSpan((int)byteLength);
        ValidateBytes(bytes, encoding, budget, hashChunkBytes);
        budget.Check();
        var result = encoding == Utf8Encoding ? StrictUtf8.GetString(bytes) : SampleChunkUtf16.Decode(bytes, budget, hashChunkBytes);
        budget.Check();
        return result;
    }

    private static int MeasureText(string value, ReadExecutionBudget budget, int textCancellationCheckIntervalCodeUnits,
        out byte encoding)
    {
        const int Utf8LengthInitialValue = 0;
        const int NextCheckInitialValue = 0;
        const int IndexInitialValue = 0;

        if (value.Length > SampleChunkWire.MaximumEncodedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SampleChunkWire.ExcessBytes);
        }
        var hasUnpairedSurrogate = false;
        long utf8Length = Utf8LengthInitialValue;
        var nextCheck = NextCheckInitialValue;
        for (var index = IndexInitialValue; index < value.Length; index++)
        {
            if (index >= nextCheck)
            {
                budget.Check();
                nextCheck = index + textCancellationCheckIntervalCodeUnits;
            }
            utf8Length += MeasureCodeUnit(value, ref index, ref hasUnpairedSurrogate);
        }
        budget.Check();
        encoding = hasUnpairedSurrogate ? Utf16Encoding : Utf8Encoding;
        return encoding == Utf16Encoding ? checked(value.Length * sizeof(char)) : checked((int)utf8Length);
    }

    private static int MeasureCodeUnit(string value, ref int index, ref bool hasUnpairedSurrogate)
    {
        const int NextCodeUnitOffset = 1;
        const int LowSurrogateOffset = 1;
        const int SurrogatePairUtf8Bytes = 4;
        const int UnpairedSurrogateUtf8Bytes = 0;
        const int OneByteUtf8MaximumCodeUnit = 0x7F;
        const int OneByteUtf8Length = 1;
        const int TwoByteUtf8MaximumCodeUnit = 0x7FF;
        const int TwoByteUtf8Length = 2;
        const int ThreeByteUtf8Length = 3;

        var current = value[index];
        if (char.IsHighSurrogate(current) && index + NextCodeUnitOffset < value.Length && char.IsLowSurrogate(value[index + LowSurrogateOffset]))
        {
            index++;
            return SurrogatePairUtf8Bytes;
        }
        if (char.IsHighSurrogate(current) || char.IsLowSurrogate(current))
        {
            hasUnpairedSurrogate = true;
            return UnpairedSurrogateUtf8Bytes;
        }
        return current <= OneByteUtf8MaximumCodeUnit ? OneByteUtf8Length : current <= TwoByteUtf8MaximumCodeUnit ? TwoByteUtf8Length : ThreeByteUtf8Length;
    }

    private static void ValidateBytes(ReadOnlySpan<byte> bytes, byte encoding, ReadExecutionBudget budget, int hashChunkBytes)
    {
        if (encoding == Utf8Encoding)
        {
            ValidateUtf8(bytes, budget, hashChunkBytes);
        }
        else
        {
            SampleChunkUtf16.ValidateFallback(bytes, budget, hashChunkBytes);
        }
    }

    private static void ValidateUtf8(ReadOnlySpan<byte> bytes, ReadExecutionBudget budget, int hashChunkBytes)
    {
        const int OffsetInitialValue = 0;
        const int Utf16CodeUnitBytes = 2;
        const int NoConsumedUtf8Bytes = 0;
        const int MinimumCodeUnitStride = 1;

        var offset = OffsetInitialValue;
        var checkStride = Math.Max(MinimumCodeUnitStride, hashChunkBytes / Utf16CodeUnitBytes);
        var nextCheck = checkStride;
        while (offset < bytes.Length)
        {
            if (offset >= nextCheck)
            {
                budget.Check();
                nextCheck = offset + checkStride;
            }
            var status = Rune.DecodeFromUtf8(bytes[offset..], out _, out var consumed);
            SampleChunkWire.Require(status == OperationStatus.Done && consumed > NoConsumedUtf8Bytes);
            offset += consumed;
        }
        budget.Check();
    }

}
