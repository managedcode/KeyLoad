namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWire
{
    private const int MinimumEncodedBytes = 1;

    private const int ZigZagSignFlagBits = 1;
    private const int Int64SignBitIndex = 63;
    private const int ZigZagSignFlagMask = 1;

    internal const string PayloadAlias = "keyload.core.v1.SampleChunkPayload";
    internal const int CurrentVersion = 1;
    internal const int MaximumRecords = 256;
    internal const int MaximumEncodedBytes = 8_388_608;
    internal const int ChecksumBytes = 32;
    internal const string InvalidMaximum = "The sample chunk byte limit is outside its supported bounds.";
    internal const string InvalidContent = "The sample chunk contains invalid sample content.";
    internal const string InvalidShape = "The sample chunk payload is malformed.";
    internal const string UnsupportedVersion = "The sample chunk format is unsupported.";
    internal const string ExcessRecords = "The sample chunk record limit is exceeded.";
    internal const string ExcessBytes = "The sample chunk byte limit is exceeded.";

    internal static void ValidateMaximum(int maximumBytes)
    {
        if (maximumBytes is < MinimumEncodedBytes or > MaximumEncodedBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidMaximum);
        }
    }

    internal static int VarUIntLength(ulong value)
    {
        const int LengthInitialValue = 1;
        const int VarUIntContinuationMask = 0x80;
        const int VarUIntPayloadBits = 7;

        var length = LengthInitialValue;
        while (value >= VarUIntContinuationMask)
        {
            value >>= VarUIntPayloadBits;
            length++;
        }
        return length;
    }

    internal static void WriteVarUInt(Span<byte> destination, ref int position, ulong value)
    {
        const int VarUIntContinuationMask = 0x80;
        const int VarUIntPayloadBits = 7;

        while (value >= VarUIntContinuationMask)
        {
            destination[position++] = (byte)(value | VarUIntContinuationMask);
            value >>= VarUIntPayloadBits;
        }
        destination[position++] = (byte)value;
    }

    internal static ulong ZigZag(long value)
        => unchecked((ulong)((value << ZigZagSignFlagBits) ^ (value >> Int64SignBitIndex)));

    internal static long UnZigZag(ulong value)
        => unchecked((long)(value >> ZigZagSignFlagBits) ^ -((long)value & ZigZagSignFlagMask));

    internal static void Require(bool condition)
    {
        if (!condition)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidShape);
        }
    }

    internal static void AddSize(ref long total, long amount)
    {
        const int AmountValidationBoundary = 0;

        if (amount < AmountValidationBoundary || amount > MaximumEncodedBytes - total)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ExcessBytes);
        }
        total += amount;
    }
}
