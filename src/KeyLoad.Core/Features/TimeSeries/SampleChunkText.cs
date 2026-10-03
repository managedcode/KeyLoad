using System.Buffers;
using System.Text;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkText
{
    private const byte Utf8Encoding = 0;
    private const byte Utf16Encoding = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static long FramedSize(string value, ReadExecutionBudget budget)
    {
        var byteLength = MeasureText(value, budget, out var encoding);
        var prefix = checked(((ulong)byteLength << 1) | encoding);
        return SampleChunkWire.VarUIntLength(prefix) + byteLength;
    }

    internal static void WriteFramed(string value, Span<byte> destination, ref int position,
        ReadExecutionBudget budget)
    {
        var byteLength = MeasureText(value, budget, out var encoding);
        var prefix = checked(((ulong)byteLength << 1) | encoding);
        SampleChunkWire.WriteVarUInt(destination, ref position, prefix);
        var target = destination.Slice(position, byteLength);
        budget.Check();
        if (encoding == Utf16Encoding)
        {
            SampleChunkUtf16.Write(value, target, budget);
        }
        else
        {
            _ = StrictUtf8.GetBytes(value.AsSpan(), target);
            budget.Check();
        }
        position += byteLength;
    }

    internal static long ValidateFramed(ref SampleChunkReader reader, ReadExecutionBudget budget)
    {
        var prefix = reader.ReadVarUInt();
        var encoding = (byte)(prefix & 1);
        var byteLength = prefix >> 1;
        SampleChunkWire.Require(byteLength <= int.MaxValue);
        var bytes = reader.ReadSpan((int)byteLength);
        ValidateBytes(bytes, encoding, budget);
        return checked((long)byteLength);
    }

    internal static string ReadFramed(ref SampleChunkReader reader, ReadExecutionBudget budget)
    {
        var prefix = reader.ReadVarUInt();
        var encoding = (byte)(prefix & 1);
        var byteLength = prefix >> 1;
        SampleChunkWire.Require(byteLength <= int.MaxValue);
        var bytes = reader.ReadSpan((int)byteLength);
        ValidateBytes(bytes, encoding, budget);
        budget.Check();
        var result = encoding == Utf8Encoding ? StrictUtf8.GetString(bytes) : SampleChunkUtf16.Decode(bytes, budget);
        budget.Check();
        return result;
    }

    private static int MeasureText(string value, ReadExecutionBudget budget, out byte encoding)
    {
        if (value.Length > SampleChunkWire.MaximumEncodedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SampleChunkWire.ExcessBytes);
        }
        var hasUnpairedSurrogate = false;
        long utf8Length = 0;
        var nextCheck = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (index >= nextCheck)
            {
                budget.Check();
                nextCheck = index + 0x3FFF;
            }
            utf8Length += MeasureCodeUnit(value, ref index, ref hasUnpairedSurrogate);
        }
        budget.Check();
        encoding = hasUnpairedSurrogate ? Utf16Encoding : Utf8Encoding;
        return encoding == Utf16Encoding ? checked(value.Length * sizeof(char)) : checked((int)utf8Length);
    }

    private static int MeasureCodeUnit(string value, ref int index, ref bool hasUnpairedSurrogate)
    {
        var current = value[index];
        if (char.IsHighSurrogate(current) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
        {
            index++;
            return 4;
        }
        if (char.IsHighSurrogate(current) || char.IsLowSurrogate(current))
        {
            hasUnpairedSurrogate = true;
            return 0;
        }
        return current <= 0x7F ? 1 : current <= 0x7FF ? 2 : 3;
    }

    private static void ValidateBytes(ReadOnlySpan<byte> bytes, byte encoding, ReadExecutionBudget budget)
    {
        if (encoding == Utf8Encoding)
        {
            ValidateUtf8(bytes, budget);
        }
        else
        {
            SampleChunkUtf16.ValidateFallback(bytes, budget);
        }
    }

    private static void ValidateUtf8(ReadOnlySpan<byte> bytes, ReadExecutionBudget budget)
    {
        var offset = 0;
        var nextCheck = SampleChunkWire.HashChunkBytes / 2;
        while (offset < bytes.Length)
        {
            if (offset >= nextCheck)
            {
                budget.Check();
                nextCheck = offset + SampleChunkWire.HashChunkBytes / 2;
            }
            var status = Rune.DecodeFromUtf8(bytes[offset..], out _, out var consumed);
            SampleChunkWire.Require(status == OperationStatus.Done && consumed > 0);
            offset += consumed;
        }
        budget.Check();
    }

}
