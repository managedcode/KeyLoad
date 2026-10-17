using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkUtf16
{
    private const int MinimumCodeUnitStride = 1;

    internal static void Write(string value, Span<byte> destination, SampleChunkWork budget, int hashChunkBytes)
    {
        const int IndexInitialValue = 0;

        var checkStride = Math.Max(MinimumCodeUnitStride, hashChunkBytes / sizeof(char));
        var nextCheck = IndexInitialValue;

        for (var index = IndexInitialValue; index < value.Length; index++)
        {
            if (index >= nextCheck)
            {
                budget.Check();
                nextCheck = index + checkStride;
            }
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(index * sizeof(char)), value[index]);
        }
        budget.Check();
    }

    internal static string Decode(ReadOnlySpan<byte> bytes, SampleChunkWork budget, int hashChunkBytes)
    {
        const int IndexInitialValue = 0;
        const int StartEmptyCount = 0;

        if (BitConverter.IsLittleEndian)
        {
            return new string(MemoryMarshal.Cast<byte, char>(bytes));
        }
        var characterCount = bytes.Length / sizeof(char);
        var characters = ArrayPool<char>.Shared.Rent(characterCount);
        var checkStride = Math.Max(MinimumCodeUnitStride, hashChunkBytes / sizeof(char));
        var nextCheck = IndexInitialValue;
        try
        {
            for (var index = IndexInitialValue; index < characterCount; index++)
            {
                if (index >= nextCheck)
                {
                    budget.Check();
                    nextCheck = index + checkStride;
                }
                characters[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(index * sizeof(char))..]);
            }
            return new string(characters.AsSpan(StartEmptyCount, characterCount));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(characters, clearArray: true);
        }
    }

    internal static void ValidateFallback(ReadOnlySpan<byte> bytes, SampleChunkWork budget, int hashChunkBytes)
    {
        const int EmptyTextBytes = 0;
        const int OddByteLengthMask = 1;
        const int OffsetInitialValue = 0;

        SampleChunkWire.Require(bytes.Length > EmptyTextBytes && (bytes.Length & OddByteLengthMask) == EmptyTextBytes);
        var pendingHigh = false;
        var hasUnpaired = false;
        var checkStride = Math.Max(MinimumCodeUnitStride, hashChunkBytes / sizeof(char)) * sizeof(char);
        var nextCheck = OffsetInitialValue;
        for (var offset = OffsetInitialValue; offset < bytes.Length; offset += sizeof(char))
        {
            if (offset >= nextCheck)
            {
                budget.Check();
                nextCheck = offset + checkStride;
            }
            var current = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
            if (pendingHigh)
            {
                if (char.IsLowSurrogate(current))
                {
                    pendingHigh = false;
                    continue;
                }
                hasUnpaired = true;
                pendingHigh = false;
            }
            if (char.IsHighSurrogate(current))
            {
                pendingHigh = true;
            }
            else if (char.IsLowSurrogate(current))
            {
                hasUnpaired = true;
            }
        }
        SampleChunkWire.Require(hasUnpaired || pendingHigh);
        budget.Check();
    }
}
